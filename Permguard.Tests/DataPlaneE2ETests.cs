// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Xunit;
using PdpAction = Permguard.Action;
using PdpClient = Permguard.Client;

namespace Permguard.Tests;

/// <summary>
/// Against a real data plane: <c>scripts/e2e-server.sh</c> starts it from the Permguard release
/// PERMGUARD_CONTRACT pins, serving a ledger <c>e2e/policies</c> that permits <c>User:alice</c> to
/// <c>read</c> (BUG-0077). It runs only when E2E_HTTP and E2E_GRPC name that data plane:
/// <c>dotnet test --filter Category=E2E</c>.
/// </summary>
public sealed class DataPlaneE2ETests
{
    private sealed class E2EFactAttribute : FactAttribute
    {
        public E2EFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_HTTP"))
                || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("E2E_GRPC")))
            {
                Skip = "E2E_HTTP and E2E_GRPC name the data plane scripts/e2e-server.sh started";
            }
        }
    }

    private static Permguard.EvaluateRequest Ask(string id) => new("e2e", "policies")
    {
        Subject = new Entity("User", id),
        Resource = new Entity("Document", "d"),
        Action = new PdpAction("read"),
    };

    /// <summary>
    /// Over both transports: a permit with its decision evidence, a deny, a batch, a refusal, a
    /// request for a signed response, and the discovery document (BUG-0077, BUG-0274).
    /// </summary>
    [E2EFact]
    [Trait("Category", "E2E")]
    public async Task AgainstARealDataPlane()
    {
        foreach (var variable in new[] { "E2E_HTTP", "E2E_GRPC" })
        {
            await using var client = new PdpClient(Environment.GetEnvironmentVariable(variable)!);

            var permit = await client.EvaluateAsync(Ask("alice"));
            Assert.True(permit.Decision, $"{variable}: alice reads");
            Assert.False(string.IsNullOrEmpty(permit.Context?.Evidence?.Status), $"{variable}: the decision's evidence");
            Assert.False((await client.EvaluateAsync(Ask("bob"))).Decision, $"{variable}: bob reads");

            var many = await client.EvaluateManyAsync(Ask("alice") with
            {
                Evaluations =
                [
                    new Evaluation { Subject = new Entity("User", "alice") },
                    new Evaluation { Subject = new Entity("User", "bob") },
                ],
            });
            Assert.Equal(new[] { true, false }, many.Evaluations!.Select(item => item.Decision));

            var unknown = await Assert.ThrowsAsync<Refusal>(
                () => client.EvaluateAsync(Ask("alice") with { Ledger = "nope" }));
            Assert.Equal("ledger_not_served", unknown.Code);

            // The nonce reaches the plane: a signed response is addressed to an authenticated
            // caller, and this one presents no token, so the plane refuses rather than answer
            // unsigned. The attestation's mapping is the unit tests'.
            var unsigned = await Assert.ThrowsAsync<Refusal>(
                () => client.EvaluateAsync(Ask("alice") with { RequestNonce = "ZTJlLW5vbmNlLTE2LWJ5dGVz" }));
            Assert.Equal("unauthenticated", unsigned.Code);

            var configuration = await client.GetConfigurationAsync();
            Assert.False(string.IsNullOrEmpty(configuration.Version), $"{variable}: the version");
            Assert.False(string.IsNullOrEmpty(configuration.Stage), $"{variable}: the stage");
            Assert.True(configuration.Limits.GetValueOrDefault("max_evaluations") > 0, $"{variable}: the limits");
            Assert.False(string.IsNullOrEmpty(configuration.ResponseSigning.State), $"{variable}: the response signing");
        }
    }
}
