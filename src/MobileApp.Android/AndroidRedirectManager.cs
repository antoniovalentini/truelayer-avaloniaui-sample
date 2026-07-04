using System;
using Microsoft.Extensions.Logging;

namespace MobileApp.Android;

public class AndroidRedirectManager(IBrowserService browser, ILogger<AndroidRedirectManager> logger) : IRedirectManager
{
    public string RedirectUri => "mysecureapp://oauth2redirect";

    public void NavigateToRedirectUri(Uri uri)
    {
        browser.OpenUrl(uri.AbsoluteUri);
    }

    public void OnRedirectSuccess(object? sender, CallbackReceivedEventArgs? args)
    {
        logger.LogInformation("Redirect successful");
    }
}
