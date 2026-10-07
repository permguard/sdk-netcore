// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;
using Grpc.Net.Client;
using Proto = Permguard.Internal.Grpc.V1;

namespace Permguard;

internal sealed class GrpcTransport : ITransport
{
    private readonly GrpcChannel channel;
    private readonly Proto.PolicyDecisionPoint.PolicyDecisionPointClient client;
    private readonly Metadata headers;

    internal GrpcTransport(Uri endpoint, ClientOptions options)
    {
        if (endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0 ||
            endpoint.UserInfo.Length != 0)
            throw new ArgumentException(
                "gRPC Permguard endpoint must not contain credentials, a path, query, or fragment.");

        var scheme = endpoint.Scheme.Equals("grpcs", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
        var address = new Uri($"{scheme}://{endpoint.Authority}");
        channel = options.GrpcChannelOptions is null
            ? GrpcChannel.ForAddress(address)
            : GrpcChannel.ForAddress(address, options.GrpcChannelOptions);
        client = new Proto.PolicyDecisionPoint.PolicyDecisionPointClient(channel);
        headers = new Metadata();
        foreach (var header in options.Headers) headers.Add(header.Key.ToLowerInvariant(), header.Value);
    }

    public async Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, bool many, DateTime deadline, CancellationToken cancellationToken)
    {
        try
        {
            var proto = GrpcMapper.ToProto(request);
            var response = many
                ? await client.EvaluateManyAsync(proto, headers, deadline, cancellationToken).ResponseAsync.ConfigureAwait(false)
                : await client.EvaluateAsync(proto, headers, deadline, cancellationToken).ResponseAsync.ConfigureAwait(false);
            return GrpcMapper.FromProto(response);
        }
        catch (RpcException error) { throw ToRefusal(error); }
    }

    public async Task<Configuration> GetConfigurationAsync(DateTime deadline, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.GetConfigurationAsync(
                new Proto.GetConfigurationRequest(), headers, deadline, cancellationToken).ResponseAsync.ConfigureAwait(false);
            return GrpcMapper.FromProto(response);
        }
        catch (RpcException error) { throw ToRefusal(error); }
    }

    private static Refusal ToRefusal(RpcException error) => new(
        error.Trailers.GetValue("permguard-error-class") ?? ClassFor(error.StatusCode),
        error.Trailers.GetValue("permguard-error-code") ?? error.StatusCode.ToString().ToLowerInvariant(),
        error.Status.Detail,
        grpcStatus: error.StatusCode,
        innerException: error);

    private static string ClassFor(StatusCode status) => status switch
    {
        StatusCode.InvalidArgument or StatusCode.OutOfRange => "validation",
        StatusCode.FailedPrecondition or StatusCode.AlreadyExists or StatusCode.Aborted => "conflict",
        StatusCode.Unauthenticated or StatusCode.PermissionDenied => "authorization",
        StatusCode.NotFound => "not_found",
        StatusCode.Unavailable or StatusCode.DeadlineExceeded => "unavailable",
        _ => "internal",
    };

    public ValueTask DisposeAsync() { channel.Dispose(); return ValueTask.CompletedTask; }
}
