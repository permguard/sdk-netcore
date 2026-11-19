// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using Grpc.Core;

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

    private Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, bool many, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        Values.Check(request);
        return Bounded((deadline, token) => transport.EvaluateAsync(request, many, deadline, token), cancellationToken);
    }

    public Task<Configuration> GetConfigurationAsync(CancellationToken cancellationToken = default) =>
        Bounded((deadline, token) => transport.GetConfigurationAsync(deadline, token), cancellationToken);

    /// <summary>
    /// Runs a call within the client's timeout. A call that fails in the client is one refusal on both
    /// transports (BUG-0284): its timeout passed, <c>unavailable</c>/<c>timeout</c>; its data plane not
    /// reached, <c>unavailable</c>/<c>unreachable</c>. The caller's own cancellation stays an
    /// <see cref="OperationCanceledException"/>.
    /// </summary>
    private async Task<T> Bounded<T>(
        Func<DateTime, CancellationToken, Task<T>> call, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            return await call(DateTime.UtcNow.Add(timeout), timeoutSource.Token).ConfigureAwait(false);
        }
        catch (Exception error) when (cancellationToken.IsCancellationRequested &&
                                      error is OperationCanceledException or RpcException)
        {
            throw new OperationCanceledException("The Permguard call was cancelled by its caller.", error,
                cancellationToken);
        }
        catch (Exception error) when (error is OperationCanceledException ||
                                      error is RpcException rpc && GrpcTransport.ClientSide(rpc) &&
                                      rpc.StatusCode is StatusCode.Cancelled or StatusCode.DeadlineExceeded)
        {
            throw Refusal.TimedOut(error);
        }
        catch (RpcException error) when (GrpcTransport.ClientSide(error) && error.StatusCode == StatusCode.Unavailable)
        {
            throw Refusal.NotReached(error);
        }
        catch (HttpRequestException error) when (error.InnerException is System.Net.Sockets.SocketException)
        {
            throw Refusal.NotReached(error);
        }
    }

    public ValueTask DisposeAsync() => transport.DisposeAsync();
}
