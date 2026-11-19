// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Permguard;
using PdpAction = Permguard.Action;

var endpoint = args.FirstOrDefault()
    ?? Environment.GetEnvironmentVariable("PERMGUARD_PDP_URL")
    ?? "grpc://localhost:7443";
await using var client = new Client(endpoint);

try
{
    var response = await client.EvaluateAsync(new EvaluateRequest("acme", "main-ledger")
    {
        Subject = new Entity("User", "alice"),
        Resource = new Entity("Document", "budget-2026"),
        Action = new PdpAction("read"),
        RequestId = Guid.NewGuid().ToString(),
    });
    Console.WriteLine(response.Decision ? "PERMIT" : "DENY");
}
catch (Refusal refusal)
{
    // A refusal is an answer too: said, not an unhandled exception (BUG-0315).
    Console.Error.WriteLine($"refused: class={refusal.ErrorClass} code={refusal.Code} message={refusal.Detail}");
    return 1;
}
return 0;
