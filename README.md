<!-- Copyright (c) 2022 Nitro Agility S.r.l. -->
<!-- SPDX-License-Identifier: Apache-2.0 -->

# Permguard .NET SDK

The official .NET 8 client for the native, stateless Permguard PDP v1 interface. The same API
supports HTTP/JSON and gRPC; the endpoint scheme selects the transport.

## Install

```console
dotnet add package Permguard
```

## Evaluate

```csharp
using Permguard;
using PdpAction = Permguard.Action;

// Use http(s):// for JSON or grpc(s):// for gRPC.
await using var client = new Client("http://127.0.0.1:9094");

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
```

For gRPC, only the endpoint changes:

```csharp
await using var client = new Client("grpc://127.0.0.1:9094");
```

`EvaluateManyAsync` sends boxcarred evaluations to `/access/v1/evaluations` or the gRPC
`EvaluateMany` method. `GetConfigurationAsync` reads the PDP discovery document. Static headers,
the per-call timeout, a custom `HttpClient`, and `GrpcChannelOptions` can be supplied through
`ClientOptions`.

HTTP refusals and gRPC status failures are exposed as `Refusal`, including the stable Permguard
error class and code. A normal deny remains a successful `EvaluateResponse` with `Decision == false`.

## Development

```console
dotnet restore SamplePermguard.sln
dotnet test SamplePermguard.sln --configuration Release -m:1
dotnet pack Permguard/Permguard.csproj --configuration Release
```

The NuGet package includes `LICENSE`, `NOTICE.md`, and `THIRD_PARTY_NOTICES.md`.
