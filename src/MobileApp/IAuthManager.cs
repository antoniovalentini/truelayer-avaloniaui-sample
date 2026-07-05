using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MobileApp;

public interface IAuthManager
{
    event EventHandler<CallbackReceivedEventArgs>? CallbackReceived;
    void Start();
    void Stop();
    void Dispose();
}

public class AuthManager : IDisposable, IAuthManager
{
    private readonly HttpListener _listener;
    private readonly ILogger<AuthManager> _logger;

    public event EventHandler<CallbackReceivedEventArgs>? CallbackReceived;
    private CancellationTokenSource? _cancellationTokenSource;

    public AuthManager(ILogger<AuthManager> logger)
    {
        _logger = logger;

        // TODO: make this configurable
        var webAuthParameters = new WebAuthParameters("localhost", 3000);

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://{webAuthParameters.Host}:" + webAuthParameters.Port + "/callback/");
    }

    public void Start()
    {
        _listener.Start();
        _ = DoStuff();
    }

    private async Task DoStuff()
    {
        _cancellationTokenSource = new CancellationTokenSource();

        var contextTask = _listener.GetContextAsync();
        var result = await Task.WhenAny(contextTask, new CancellationTokenTaskSource<HttpListenerContext>(_cancellationTokenSource.Token).Task);

        if (result.IsCanceled)
        {
            _logger.LogWarning("Stopping application...");
            _listener.Stop();
            return;
        }

        var context = await result;

        const string responseString = "<html><body>Please return to the app.</body></html>";
        var buffer = Encoding.UTF8.GetBytes(responseString);
        context.Response.ContentLength64 = buffer.Length;

        context.Response.StatusCode = (int) HttpStatusCode.OK;
        context.Response.ContentType = "text/html";
        await context.Response.OutputStream.WriteAsync(buffer.AsMemory(0, buffer.Length), _cancellationTokenSource.Token);
        context.Response.OutputStream.Close();
        _listener.Stop();

        var queryParams = new Dictionary<string, string>();
        foreach (string? s in context.Request.QueryString)
        {
            if (s is null) continue;
            var value = context.Request.QueryString[s];
            if (value is null) continue;
            queryParams[s] = value;
        }

        _logger.LogInformation("Received auth callback with {ParamCount} query parameters", queryParams.Count);
        CallbackReceived?.Invoke(this, new CallbackReceivedEventArgs(queryParams));
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
        if (_listener.IsListening) _listener.Stop();
    }

    public void Dispose()
    {
        Stop();
        _listener.Close();
        _logger.LogInformation("AuthManager disposed.");
    }
}

public record CallbackReceivedEventArgs(Dictionary<string, string> QueryParams);

public record WebAuthParameters(string Host, int Port);
