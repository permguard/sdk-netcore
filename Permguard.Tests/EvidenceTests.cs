// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Permguard.Internal.Grpc.V1;
using Xunit;
using PdpClient = Permguard.Client;
using Proto = Permguard.Internal.Grpc.V1;

namespace Permguard.Tests;

public sealed class EvidenceTests
{
    private const string Nonce = "bm9uY2Utb2Ytc2l4dGVlbg";

    private static readonly Evidence ExpectedEvidence = new("required", "recorded")
    {
        RecordId = "rec-1",
        Position = new EvidencePosition("h", "r", "7"),
    };

    private static readonly Attestation ExpectedAttestation = new("p", "b", "s");

    /// <summary>
    /// BUG-0274: a request asks for a signed response with its nonce, and the answer's attestation
    /// and decision evidence reach the caller over both transports.
    /// </summary>
    [Fact]
    public async Task TheNonceTheAttestationAndTheEvidenceTravelOnBothTransports()
    {
        var request = new Permguard.EvaluateRequest("z", "l") { RequestNonce = Nonce };

        await using var server = await SigningServer.StartAsync();
        await using (var grpc = new PdpClient(server.Endpoint.Replace("http://", "grpc://")))
        {
            var fromGrpc = await grpc.EvaluateAsync(request);
            Assert.Equal(Nonce, server.Pdp.SeenNonce);
            Assert.Equal(ExpectedEvidence, fromGrpc.Context?.Evidence);
            Assert.Equal(ExpectedAttestation, fromGrpc.Attestation);
        }

        var handler = new AnsweringHandler(
            """{"decision":true,"context":{"id":"d-1","evidence":{"mode":"required","status":"recorded","record_id":"rec-1","position":{"host_id":"h","run":"r","sequence":"7"}}},"attestation":{"protected":"p","payload":"b","signature":"s"}}""");
        using var http = new HttpClient(handler);
        await using var client = new PdpClient("http://pdp.example", new ClientOptions { HttpClient = http });
        var fromHttp = await client.EvaluateAsync(request);
        Assert.Contains($"\"request_nonce\":\"{Nonce}\"", handler.Seen, StringComparison.Ordinal);
        Assert.Equal(ExpectedEvidence, fromHttp.Context?.Evidence);
        Assert.Equal(ExpectedAttestation, fromHttp.Attestation);
        await client.EvaluateAsync(new Permguard.EvaluateRequest("z", "l"));
        Assert.DoesNotContain("request_nonce", handler.Seen, StringComparison.Ordinal);
    }

    /// <summary>
    /// BUG-0274: the discovery document's version, lifecycle, limits and response signing reach the
    /// caller over both transports.
    /// </summary>
    [Fact]
    public async Task TheDiscoveryDocumentIsReadWholeOnBothTransports()
    {
        await using var server = await SigningServer.StartAsync();
        await using var grpc = new PdpClient(server.Endpoint.Replace("http://", "grpc://"));
        var handler = new AnsweringHandler(
            """{"interface":"permguard.api.pdp.native.v1","pdp":"permguard","endpoints":{"evaluation":"/access/v1/evaluation","evaluations":"/access/v1/evaluations"},"capabilities":["http"],"store_scope":{"in":"request","zone":"zone","ledger":"ledger","profile":"profile"},"version":"0.1.33","component":"data-plane","state":"serving","stage":"v1","limits":{"max_evaluations":64},"response_signing":{"state":"optional","validity_seconds":300,"clock_skew_seconds":30}}""");
        using var http = new HttpClient(handler);
        await using var client = new PdpClient("http://pdp.example", new ClientOptions { HttpClient = http });

        foreach (var held in new[] { await grpc.GetConfigurationAsync(), await client.GetConfigurationAsync() })
        {
            Assert.Equal("0.1.33", held.Version);
            Assert.Equal("data-plane", held.Component);
            Assert.Equal("serving", held.State);
            Assert.Equal("v1", held.Stage);
            Assert.Null(held.Sunset);
            Assert.Equal(64UL, held.Limits["max_evaluations"]);
            Assert.Equal(
                new ResponseSigning("optional") { ValiditySeconds = 300, ClockSkewSeconds = 30 },
                held.ResponseSigning);
        }
    }

    private sealed class AnsweringHandler(string answer) : HttpMessageHandler
    {
        internal string Seen { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Seen = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(answer, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class SigningServer : IAsyncDisposable
    {
        private readonly WebApplication application;

        private SigningServer(WebApplication application, string endpoint, SigningPdp pdp)
        {
            this.application = application;
            Endpoint = endpoint;
            Pdp = pdp;
        }

        internal string Endpoint { get; }
        internal SigningPdp Pdp { get; }

        internal static async Task<SigningServer> StartAsync()
        {
            var pdp = new SigningPdp();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(
                IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc();
            builder.Services.AddSingleton(pdp);
            var application = builder.Build();
            application.MapGrpcService<SigningPdp>();
            await application.StartAsync();
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            return new SigningServer(application, addresses.Single(), pdp);
        }

        public async ValueTask DisposeAsync() => await application.DisposeAsync();
    }

    private sealed class SigningPdp : PolicyDecisionPoint.PolicyDecisionPointBase
    {
        internal string? SeenNonce { get; private set; }

        public override Task<Proto.EvaluateResponse> Evaluate(Proto.EvaluateRequest request, ServerCallContext context)
        {
            SeenNonce = request.RequestNonce;
            return Task.FromResult(new Proto.EvaluateResponse
            {
                Decision = true,
                Context = new Proto.DecisionContext
                {
                    Id = "d-1",
                    Evidence = new Proto.Evidence
                    {
                        Mode = "required",
                        Status = "recorded",
                        RecordId = "rec-1",
                        Position = new Proto.EvidencePosition { HostId = "h", Run = "r", Sequence = "7" },
                    },
                },
                Attestation = new Proto.Attestation { Protected = "p", Payload = "b", Signature = "s" },
            });
        }

        public override Task<GetConfigurationResponse> GetConfiguration(
            GetConfigurationRequest request, ServerCallContext context) => Task.FromResult(new GetConfigurationResponse
            {
                Interface = "permguard.api.pdp.native.v1",
                Version = "0.1.33",
                Component = "data-plane",
                State = "serving",
                Stage = "v1",
                Limits = { ["max_evaluations"] = 64 },
                ResponseSigning = new Proto.ResponseSigning { State = "optional", ValiditySeconds = 300, ClockSkewSeconds = 30 },
            });
    }
}
