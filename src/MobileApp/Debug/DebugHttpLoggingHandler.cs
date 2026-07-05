using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MobileApp.Debug;

public sealed class DebugHttpLoggingHandler(IDebugStore<DebugNetworkEntry> store) : DelegatingHandler
{
    // Mirrors the internal TrueLayer.CustomHeaders.TraceId constant (not accessible from this assembly).
    private const string TraceIdHeader = "Tl-Trace-Id";
    private const int MaxErrorSnippetLength = 500;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var method = request.Method.Method;
        var uri = request.RequestUri?.ToString() ?? "(unknown)";

        try
        {
            var response = await base.SendAsync(request, cancellationToken);
            stopwatch.Stop();

            string? error = null;
            if (!response.IsSuccessStatusCode)
            {
                // Buffer the content so both this handler and the real caller (TrueLayer's ApiClient)
                // can read the body — an unbuffered stream can only be read once. Only decode a
                // capped snippet here so a large error body doesn't get materialized as a full string
                // just to be truncated.
                await response.Content.LoadIntoBufferAsync();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var reader = new StreamReader(stream);
                var buffer = new char[MaxErrorSnippetLength];
                var read = await reader.ReadBlockAsync(buffer, cancellationToken);
                error = new string(buffer, 0, read);
            }

            var traceId = response.Headers.TryGetValues(TraceIdHeader, out var values) ? values.FirstOrDefault() : null;

            store.Add(new DebugNetworkEntry(DateTimeOffset.UtcNow, method, uri, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, traceId, error));
            return response;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            store.Add(new DebugNetworkEntry(DateTimeOffset.UtcNow, method, uri, null, stopwatch.ElapsedMilliseconds, null, ex.Message));
            throw;
        }
    }
}
