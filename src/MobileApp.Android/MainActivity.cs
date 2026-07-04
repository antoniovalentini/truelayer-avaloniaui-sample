using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Runtime;
using Avalonia;
using Avalonia.Android;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileApp.Models;
using AndroidContent = Android.Content;

namespace MobileApp.Android;

[Activity(
    Label = "TrueMobile",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTask,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)
]
[IntentFilter(
    [AndroidContent.Intent.ActionView],
    Categories =
    [
        AndroidContent.Intent.CategoryDefault,
        AndroidContent.Intent.CategoryBrowsable
    ],
    DataScheme = "mysecureapp",
    DataHost = "oauth2redirect",
    DataPathPrefix = "",
    AutoVerify = true
)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnNewIntent(AndroidContent.Intent? intent)
    {
        base.OnNewIntent(intent);

        HandleIntent(intent);
    }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        if (Intent?.Data is not null)
        {
            HandleIntent(Intent);
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (IsFinishing)
        {
            App.Instance.Services.Dispose();
        }
    }

    private void HandleIntent(AndroidContent.Intent? intent)
    {
        var logger = App.Instance.Services.GetRequiredService<ILogger<MainActivity>>();
        logger.LogInformation("Handle Deep Link Intent");
        if (intent is null)
        {
            logger.LogWarning("Received null intent in OnNewIntent");
            return;
        }

        var uri = intent.DataString;
        if (string.IsNullOrEmpty(uri))
        {
            logger.LogWarning("Received null or empty URI in OnNewIntent");
            return;
        }

        var parsed = global::Android.Net.Uri.Parse(uri);
        if (parsed?.Host != "oauth2redirect")
        {
            logger.LogWarning("Received unexpected host in OnNewIntent: {Host}", parsed?.Host);
            return;
        }

        var queryParams = new Dictionary<string, string>();
        if (parsed.QueryParameterNames != null)
            foreach (var param in parsed.QueryParameterNames)
            {
                var value = parsed.GetQueryParameter(param);
                if (value is null) continue;
                queryParams[param] = value;
            }

        logger.LogInformation("Received redirect callback with {ParamCount} query parameters: {Uri}", queryParams.Count, uri);
        var messenger = App.Instance.Services.GetRequiredService<IMessenger>();
        messenger.Send(new CallbackReceivedMessage(new CallbackReceivedEventArgs(queryParams)));
    }
}

[Application]
public class Application : AvaloniaAndroidApplication<AndroidApp>
{
    protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
    {
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}

public class AndroidApp : App
{
    private readonly string[] _manifestResourceNames = Assembly
        .GetExecutingAssembly()
        .GetManifestResourceNames();

    protected override string DeviceId =>
        global::Android.Provider.Settings.Secure.GetString(
            global::Android.App.Application.Context.ContentResolver,
            global::Android.Provider.Settings.Secure.AndroidId) ?? base.DeviceId;

    protected override string DeviceName => Build.Manufacturer + " " + Build.Model;
    protected override string DeviceType => $"Android {Build.VERSION.Release} (SDK {Build.VERSION.SdkInt})";

    protected override string ReadResourceFile(string resourceName)
    {
        using var stream = ReadResourceStream(resourceName);
        using var streamReader = new StreamReader(stream);
        return streamReader.ReadToEnd();
    }

    private Stream ReadResourceStream(string resourceName)
    {
        var appsettingsResName = _manifestResourceNames.FirstOrDefault(r => r.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        if (appsettingsResName is null)
        {
            throw new FileNotFoundException($" The configuration file '{resourceName}' was not found and is not optional.");
        }
        var resourceStream = GetType().GetTypeInfo().Assembly.GetManifestResourceStream(appsettingsResName);
        ArgumentNullException.ThrowIfNull(resourceStream);
        return resourceStream;
    }

    protected override void RegisterPlatformServices(IServiceCollection services)
    {
        services
            .AddSingleton<IBrowserService, AndroidBrowserService>()
            .AddSingleton<IRedirectManager, AndroidRedirectManager>();
    }

    protected override void PlatformConfiguration(ConfigurationBuilder builder)
    {
        builder.AddJsonStream(ReadResourceStream("appsettings.json"));
    }
}
