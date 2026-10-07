// Copyright (c) 2022 Nitro Agility S.r.l.
// SPDX-License-Identifier: Apache-2.0

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Permguard;

internal sealed class HttpTransport : ITransport
{
    private const int MaximumResponseBytes = 16 * 1024 * 1024;
    private readonly HttpClient client;
    private readonly bool ownsClient;

    internal HttpTransport(Uri endpoint, ClientOptions options)
    {
        if (endpoint.AbsolutePath != "/" || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
        {
            throw new ArgumentException("HTTP Permguard endpoint must not contain a path, query, or fragment.");
        }

        ownsClient = options.HttpClient is null;
        client = options.HttpClient ?? new HttpClient();
        client.BaseAddress = new Uri($"{endpoint.Scheme}://{endpoint.Authority}");
        foreach (var header in options.Headers)
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }
    }

    public Task<EvaluateResponse> EvaluateAsync(
        EvaluateRequest request, bool many, DateTime deadline, CancellationToken cancellationToken) =>
        CallAsync<EvaluateResponse>(HttpMethod.Post,
            many ? "/access/v1/evaluations" : "/access/v1/evaluation", request, cancellationToken);

    public Task<Configuration> GetConfigurationAsync(DateTime deadline, CancellationToken cancellationToken) =>
        CallAsync<Configuration>(HttpMethod.Get,
            "/.well-known/permguard-pdp-v1-configuration", null, cancellationToken);

    private async Task<T> CallAsync<T>(
        HttpMethod method, string path, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Accept.ParseAdd("application/json");
        if (body is not null)
        {
            request.Content = JsonContent.Create(body, options: Json.Options);
        }

        using var response = await client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using var limited = new LimitedReadStream(stream, MaximumResponseBytes);

        if (!response.IsSuccessStatusCode)
        {
            HttpError? payload = null;
            try
            {
                payload = await JsonSerializer.DeserializeAsync<HttpError>(
                    limited, Json.Options, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                // A proxy may return a non-JSON error; the status remains useful.
            }
            throw new Refusal(
                payload?.Class ?? ClassFor(response.StatusCode),
                payload?.Code ?? "http_status",
                payload?.Message ?? response.ReasonPhrase ?? "Permguard HTTP request failed",
                (int)response.StatusCode);
        }

        return await JsonSerializer.DeserializeAsync<T>(limited, Json.Options, cancellationToken)
                   .ConfigureAwait(false)
               ?? throw new InvalidDataException("Permguard returned an empty JSON response.");
    }

    private static string ClassFor(HttpStatusCode status) => status switch
    {
        HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => "validation",
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "authorization",
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout => "unavailable",
        _ => "internal",
    };

    public ValueTask DisposeAsync()
    {
        if (ownsClient) client.Dispose();
        return ValueTask.CompletedTask;
    }

    private sealed record HttpError(string Class, string Code, string Message);

    private sealed class LimitedReadStream(Stream inner, long maximumBytes) : Stream
    {
        private long read;
        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
        private int Count(int count)
        {
            read += count;
            if (read > maximumBytes)
                throw new InvalidDataException($"Permguard response exceeds {maximumBytes} bytes.");
            return count;
        }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
        public override async ValueTask DisposeAsync() { await inner.DisposeAsync().ConfigureAwait(false); GC.SuppressFinalize(this); }
    }
}
