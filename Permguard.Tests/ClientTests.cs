// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Text;
using System.Text.Json;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Permguard.Internal.Grpc.V1;
using Xunit;
using PdpAction = Permguard.Action;
using PdpClient = Permguard.Client;

namespace Permguard.Tests;

public sealed class ClientTests
{
    [Fact]
    public async Task HttpTransportUsesNativeV1PathsAndSnakeCaseJson()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        await using var client = new PdpClient("http://pdp.example", new ClientOptions
        {
            HttpClient = http,
            Headers = new Dictionary<string, string> { ["x-tenant"] = "acme" },
        });

        var response = await client.EvaluateManyAsync(Request());
        var configuration = await client.GetConfigurationAsync();

        Assert.True(response.Decision);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal("permguard.api.pdp.native.v1", configuration.Interface);
        Assert.Equal(new[]
        {
            "/access/v1/evaluations",
            "/.well-known/permguard-pdp-v1-configuration",
        }, handler.Paths);
        Assert.Contains("\"partition_inputs\":{}", handler.EvaluationJson);
        Assert.Contains("\"evaluations_semantic\":\"execute_all\"", handler.EvaluationJson);
        Assert.Equal("acme", handler.Tenant);
    }

    [Fact]
    public async Task GrpcTransportCallsNativeV1Service()
    {
        await using var server = await GrpcServer.StartAsync();
        await using var client = new PdpClient(server.Endpoint.Replace("http://", "grpc://"));

        var response = await client.EvaluateManyAsync(Request());
        var configuration = await client.GetConfigurationAsync();

        Assert.True(response.Decision);
        Assert.Equal("req-1", response.RequestId);
        Assert.Equal("permguard.api.pdp.native.v1", configuration.Interface);
        Assert.Equal("/access/v1/evaluation", configuration.Endpoints.Evaluation);
    }

    [Fact]
    public async Task GrpcRefusalPreservesPermguardMetadata()
    {
        await using var server = await GrpcServer.StartAsync();
        await using var client = new PdpClient(server.Endpoint.Replace("http://", "grpc://"));

        var refusal = await Assert.ThrowsAsync<Refusal>(() =>
            client.EvaluateAsync(Request() with { Ledger = "refuse" }));

        Assert.Equal("validation", refusal.ErrorClass);
        Assert.Equal("invalid_ledger", refusal.Code);
        Assert.Equal(StatusCode.InvalidArgument, refusal.GrpcStatus);

        var conflict = await Assert.ThrowsAsync<Refusal>(() =>
            client.EvaluateAsync(Request() with { Ledger = "conflict" }));
        Assert.Equal("conflict", conflict.ErrorClass);
        Assert.Equal(StatusCode.FailedPrecondition, conflict.GrpcStatus);
    }

    [Fact]
    public async Task HttpConflictFallbackAndEndpointValidationMatchSharedContract()
    {
        var handler = new StubHandler();
        using var http = new HttpClient(handler);
        await using var client = new PdpClient("http://pdp.example", new ClientOptions
        {
            HttpClient = http,
            Headers = new Dictionary<string, string> { ["x-tenant"] = "acme" },
        });

        var conflict = await Assert.ThrowsAsync<Refusal>(() =>
            client.EvaluateAsync(Request() with { Ledger = "conflict" }));
        Assert.Equal("conflict", conflict.ErrorClass);
        Assert.Equal(409, conflict.HttpStatus);
        Assert.Throws<ArgumentException>(() => new PdpClient("http://user:secret@pdp.example"));
    }

    /// <summary>A handler that never answers.</summary>
    private sealed class SilentHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            throw new InvalidOperationException("unreachable");
        }
    }

    private static int ClosedPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>
    /// BUG-0284: a call that fails in the client is the same refusal on HTTP and gRPC, class
    /// unavailable with code timeout or unreachable, never internal; the caller's own cancellation is
    /// an OperationCanceledException, not a refusal.
    /// </summary>
    [Fact]
    public async Task ClientFailuresAreTheSameRefusalOnBothTransports()
    {
        await using var server = await GrpcServer.StartAsync();
        using var silentHttp = new HttpClient(new SilentHandler());
        var quick = TimeSpan.FromMilliseconds(300);
        await using var http = new PdpClient("http://pdp.example", new ClientOptions { HttpClient = silentHttp, Timeout = quick });
        await using var grpc = new PdpClient(server.Endpoint.Replace("http://", "grpc://"), new ClientOptions { Timeout = quick });
        foreach (var client in new[] { http, grpc })
        {
            var refusal = await Assert.ThrowsAsync<Refusal>(() => client.EvaluateAsync(Request() with { Ledger = "silent" }));
            Assert.Equal((Refusal.Unavailable, Refusal.Timeout), (refusal.ErrorClass, refusal.Code));

            using var cancelled = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => client.EvaluateAsync(Request() with { Ledger = "silent" }, cancelled.Token));
        }

        var port = ClosedPort();
        foreach (var endpoint in new[] { $"http://127.0.0.1:{port}", $"grpc://127.0.0.1:{port}" })
        {
            await using var client = new PdpClient(endpoint, new ClientOptions { Timeout = TimeSpan.FromSeconds(3) });
            var refusal = await Assert.ThrowsAsync<Refusal>(() => client.EvaluateAsync(Request()));
            Assert.Equal((Refusal.Unavailable, Refusal.Unreachable), (refusal.ErrorClass, refusal.Code));
        }
    }

    /// <summary>
    /// BUG-0306, BUG-0307: a context value follows the server's rule before either transport sends
    /// it: 2^53 crosses, an integer past it is value_unrepresentable, NaN and infinities are
    /// payload_malformed, a typed refusal on HTTP and gRPC alike, never an unhandled exception.
    /// </summary>
    [Fact]
    public async Task ContextValuesFollowTheServersRuleOnBothTransports()
    {
        await using var server = await GrpcServer.StartAsync();
        var handler = new StubHandler();
        using var stubbed = new HttpClient(handler);
        await using var http = new PdpClient("http://pdp.example", new ClientOptions
        {
            HttpClient = stubbed,
            Headers = new Dictionary<string, string> { ["x-tenant"] = "acme" },
        });
        await using var grpc = new PdpClient(server.Endpoint.Replace("http://", "grpc://"));
        foreach (var client in new[] { http, grpc })
        {
            foreach (var value in new object[] { 9_007_199_254_740_992L, -9_007_199_254_740_992L, 0.5 })
            {
                var response = await client.EvaluateAsync(Request() with { Context = new Dictionary<string, object?> { ["n"] = value } });
                Assert.True(response.Decision);
            }
            foreach (var (value, code) in new (object, string)[]
                     {
                         (9_007_199_254_740_993L, "value_unrepresentable"),
                         (ulong.MaxValue, "value_unrepresentable"),
                         (decimal.MaxValue, "value_unrepresentable"),
                         (double.NaN, "payload_malformed"),
                         (double.PositiveInfinity, "payload_malformed"),
                     })
            {
                var refusal = await Assert.ThrowsAsync<Refusal>(() =>
                    client.EvaluateAsync(Request() with { Context = new Dictionary<string, object?> { ["n"] = value } }));
                Assert.Equal(("validation", code), (refusal.ErrorClass, refusal.Code));
            }
        }
    }

    private static Permguard.EvaluateRequest Request() => new("acme", "main")
    {
        Profile = "default",
        Subject = new Permguard.Entity("user", "amy"),
        Resource = new Permguard.Entity("document", "report"),
        Action = new PdpAction("read"),
        RequestId = "req-1",
        Evaluations = new[]
        {
            new Permguard.Evaluation
            {
                RequestId = "item-1",
                PartitionInputs = new Dictionary<string, Permguard.PartitionInput>(),
            },
        },
        Options = new Permguard.EvaluationOptions
        {
            EvaluationsSemantic = Permguard.EvaluationsSemantic.ExecuteAll,
        },
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        internal List<string> Paths { get; } = [];
        internal string EvaluationJson { get; private set; } = "";
        internal string? Tenant { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            Tenant = request.Headers.GetValues("x-tenant").Single();
            if (request.Content is not null)
                EvaluationJson = await request.Content.ReadAsStringAsync(cancellationToken);

            if (EvaluationJson.Contains("\"ledger\":\"conflict\"", StringComparison.Ordinal))
            {
                return new HttpResponseMessage(HttpStatusCode.Conflict)
                {
                    Content = new StringContent(
                        """{"code":"ledger_conflict","message":"ledger changed"}""",
                        Encoding.UTF8,
                        "application/json"),
                };
            }

            var json = request.Method == HttpMethod.Get
                ? """{"interface":"permguard.api.pdp.native.v1","pdp":"test","endpoints":{"evaluation":"/access/v1/evaluation","evaluations":"/access/v1/evaluations"},"capabilities":["grpc","http"],"store_scope":{"in":"request","zone":"zone","ledger":"ledger","profile":"profile"}}"""
                : """{"decision":true,"request_id":"req-1","evaluations":[{"decision":true,"request_id":"item-1"}]}""";
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class GrpcServer : IAsyncDisposable
    {
        private readonly WebApplication application;
        private GrpcServer(WebApplication application, string endpoint)
        {
            this.application = application;
            Endpoint = endpoint;
        }

        internal string Endpoint { get; }

        internal static async Task<GrpcServer> StartAsync()
        {
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.ConfigureKestrel(options => options.Listen(
                IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
            builder.Services.AddGrpc();
            var application = builder.Build();
            application.MapGrpcService<TestPdp>();
            application.MapGet("/", () => Results.Ok());
            await application.StartAsync();
            var addresses = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            return new GrpcServer(application, addresses.Single());
        }

        public async ValueTask DisposeAsync() => await application.DisposeAsync();
    }

    private sealed class TestPdp : PolicyDecisionPoint.PolicyDecisionPointBase
    {
        public override Task<Permguard.Internal.Grpc.V1.EvaluateResponse> Evaluate(
            Permguard.Internal.Grpc.V1.EvaluateRequest request, ServerCallContext context) => Respond(request);

        public override Task<Permguard.Internal.Grpc.V1.EvaluateResponse> EvaluateMany(
            Permguard.Internal.Grpc.V1.EvaluateRequest request, ServerCallContext context) => Respond(request);

        private static Task<Permguard.Internal.Grpc.V1.EvaluateResponse> Respond(
            Permguard.Internal.Grpc.V1.EvaluateRequest request)
        {
            if (request.Ledger == "refuse")
            {
                var trailers = new Metadata
                {
                    { "permguard-error-class", "validation" },
                    { "permguard-error-code", "invalid_ledger" },
                };
                throw new RpcException(new Status(StatusCode.InvalidArgument, "bad ledger"), trailers);
            }
            if (request.Ledger == "conflict")
                throw new RpcException(new Status(StatusCode.FailedPrecondition, "ledger changed"));
            if (request.Ledger == "silent")
                return Silent();
            return Task.FromResult(new Permguard.Internal.Grpc.V1.EvaluateResponse
            {
                Decision = true,
                RequestId = request.RequestId,
            });
        }

        private static async Task<Permguard.Internal.Grpc.V1.EvaluateResponse> Silent()
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            throw new InvalidOperationException("the client stopped waiting long before");
        }

        public override Task<GetConfigurationResponse> GetConfiguration(
            GetConfigurationRequest request, ServerCallContext context) => Task.FromResult(new GetConfigurationResponse
            {
                Interface = "permguard.api.pdp.native.v1",
                Pdp = "test",
                Endpoints = new Permguard.Internal.Grpc.V1.Endpoints
                {
                    Evaluation = "/access/v1/evaluation",
                    Evaluations = "/access/v1/evaluations",
                },
                StoreScope = new Permguard.Internal.Grpc.V1.StoreScope
                {
                    In = "request",
                    Zone = "zone",
                    Ledger = "ledger",
                    Profile = "profile",
                },
            });
    }
}
