// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Grpc.Net.Client;

namespace Permguard;

public sealed class ClientOptions
{
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyDictionary<string, string> Headers { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public HttpClient? HttpClient { get; init; }
    public GrpcChannelOptions? GrpcChannelOptions { get; init; }
}
