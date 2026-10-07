// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

namespace Permguard;

/// <summary>A client for the native, stateless Permguard PDP v1 interface.</summary>
public sealed class Client : IAsyncDisposable
{
    private readonly ITransport transport;
    private readonly TimeSpan timeout;

    public Client(string endpoint, ClientOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);
        options ??= new ClientOptions();
        if (options.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Timeout must be greater than zero.");
        }
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Host.Length == 0)
        {
            throw new ArgumentException("Permguard endpoint must be an absolute URI with a host.", nameof(endpoint));
        }

        timeout = options.Timeout;
        transport = uri.Scheme.ToLowerInvariant() switch
        {
            "http" or "https" => new HttpTransport(uri, options),
            "grpc" or "grpcs" => new GrpcTransport(uri, options),
            _ => throw new ArgumentException($"Unsupported Permguard endpoint scheme '{uri.Scheme}'.", nameof(endpoint)),
        };
    }

    public Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, CancellationToken cancellationToken = default) =>
        EvaluateAsync(request, false, cancellationToken);

    public Task<EvaluateResponse> EvaluateManyAsync(
        EvaluateRequest request, CancellationToken cancellationToken = default) =>
        EvaluateAsync(request, true, cancellationToken);

    private async Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, bool many, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return await transport.EvaluateAsync(
            request, many, DateTime.UtcNow.Add(timeout), timeoutSource.Token).ConfigureAwait(false);
    }

    public async Task<Configuration> GetConfigurationAsync(CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        return await transport.GetConfigurationAsync(
            DateTime.UtcNow.Add(timeout), timeoutSource.Token).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => transport.DisposeAsync();
}
