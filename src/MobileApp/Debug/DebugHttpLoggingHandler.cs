using System;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MobileApp.Debug;

public sealed class DebugHttpLoggingHandler(IDebugNetworkStore store) : DelegatingHandler
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

            // Buffer the content so both this handler and the real caller (TrueLayer's ApiClient)
            // can read the body — an unbuffered stream can only be read once.
            await response.Content.LoadIntoBufferAsync();

            string? error = null;
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                error = body.Length > MaxErrorSnippetLength ? body[..MaxErrorSnippetLength] : body;
            }

            var traceId = response.Headers.TryGetValues(TraceIdHeader, out var values) ? values.FirstOrDefault() : null;

            store.Add(new DebugNetworkEntry(DateTimeOffset.UtcNow, method, uri, (int)response.StatusCode, stopwatch.ElapsedMilliseconds, traceId, error));
            return response;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            store.Add(new DebugNetworkEntry(DateTimeOffset.UtcNow, method, uri, null, stopwatch.ElapsedMilliseconds, null, ex.Message));
            throw;
        }
    }
}
