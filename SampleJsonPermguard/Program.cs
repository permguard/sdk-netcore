// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Permguard;
using PdpAction = Permguard.Action;

var endpoint = args.FirstOrDefault() ?? "http://127.0.0.1:9094";
await using var client = new Client(endpoint);

var response = await client.EvaluateAsync(new EvaluateRequest("acme", "main")
{
    Profile = "default",
    Subject = new Entity("user", "amy"),
    Resource = new Entity("document", "quarterly-report"),
    Action = new PdpAction("read"),
    Context = new Dictionary<string, object?> { ["ip"] = "192.0.2.10" },
    RequestId = Guid.NewGuid().ToString(),
});

Console.WriteLine(response.Decision ? "PERMIT" : "DENY");
