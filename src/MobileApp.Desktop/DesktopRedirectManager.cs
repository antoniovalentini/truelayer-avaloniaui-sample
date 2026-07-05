using System;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using MobileApp.Models;

namespace MobileApp.Desktop;

public class DesktopRedirectManager(IBrowserService browser, IAuthManager authManager, IMessenger messenger, ILogger<DesktopRedirectManager> logger) : IRedirectManager
{
    public string RedirectUri => "http://localhost:3000/callback";

    public void NavigateToRedirectUri(Uri uri)
    {
        authManager.CallbackReceived += OnRedirectSuccess;
        authManager.Start();

        browser.OpenUrl(uri.AbsoluteUri);
    }

    public void OnRedirectSuccess(object? sender, CallbackReceivedEventArgs args)
    {
        logger.LogInformation("Desktop redirect successful with {ParamCount} query parameters", args.QueryParams.Count);
        messenger.Send(new CallbackReceivedMessage(args));
        authManager.Stop();
    }
}
