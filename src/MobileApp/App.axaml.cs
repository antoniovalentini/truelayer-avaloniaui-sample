using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MobileApp.ViewModels;
using MobileApp.Views;
using MobileApp.Debug;
using TrueLayer.Caching;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace MobileApp;

public abstract class App : Application
{
    [SuppressMessage("ReSharper", "PublicConstructorInAbstractClass", Justification = "Warning AVLN3001 Avalonia: XAML resource \"App.axaml\" won't be reachable via runtime loader")]
    public App() { }

    public static App Instance => (Current as App)!;
    public ServiceProvider Services { get; private set; } = null!;

    public static readonly ActivitySource Source = new("TrueMobile");

    public string AppVersion => (GetType().Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "unknown")
        .Split('+')[0];

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
#if DEBUG
        this.AttachDeveloperTools();
#endif
    }

    // These methods will be implemented in platform-specific projects
    protected abstract void RegisterPlatformServices(IServiceCollection services);
    protected abstract void PlatformConfiguration(ConfigurationBuilder builder);
    protected abstract string ReadResourceFile(string resourceName);
    protected virtual string DeviceId => Environment.MachineName;
    protected virtual string DeviceName => Environment.MachineName;
    protected virtual string DeviceType => Environment.OSVersion.ToString();

    public override void OnFrameworkInitializationCompleted()
    {
        if (Design.IsDesignMode)
        {
            base.OnFrameworkInitializationCompleted();
            return;
        }

#if DEBUG
        _ = new OtelDiagnosticsListener();
#endif

        var configBuilder = new ConfigurationBuilder();

        PlatformConfiguration(configBuilder);

        var config = configBuilder.Build();

        var debugLogStore = new DebugLogStore();

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var exception = args.ExceptionObject as Exception;
            debugLogStore.Add(new DebugLogEntry(DateTimeOffset.UtcNow, LogLevel.Critical, "UnhandledException", exception?.Message ?? "Unknown unhandled exception", exception?.ToString()));
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            debugLogStore.Add(new DebugLogEntry(DateTimeOffset.UtcNow, LogLevel.Critical, "UnobservedTaskException", args.Exception.Message, args.Exception.ToString()));
            args.SetObserved();
        };

        var services = new ServiceCollection();
        services
            .AddSingleton<IDebugLogStore>(debugLogStore)
            .AddLogging(builder => builder
                .AddConsole()
                .AddProvider(new InMemoryLoggerProvider(debugLogStore)))
            .AddSingleton<MainViewModel>()
            .AddSingleton<PaymentViewModel>()
            .AddSingleton<DataViewModel>()
            .AddSingleton<SettingsViewModel>()
            .AddSingleton<IMessenger>(WeakReferenceMessenger.Default)
            .AddSingleton<IAuthTokenStorage, AuthTokenStorage>()
            .AddSingleton<IAuthManager, AuthManager>()
            .AddSingleton<IPaymentService, PaymentService>()
            .AddTrueLayer(config, options =>
                {
                    if (options.Payments?.SigningKey != null)
                    {
                        options.Payments.SigningKey.PrivateKey = ReadResourceFile("ec512-private-key.pem");
                    }
                },
                authTokenCachingStrategy: AuthTokenCachingStrategies.InMemory);


        services.AddOpenTelemetry()
            .WithTracing(tracerProviderBuilder =>
            {
                tracerProviderBuilder
                    .AddSource("TrueMobile") // used inside library.name for custom activities
                    .SetSampler(new AlwaysOnSampler())
                    .SetResourceBuilder(ResourceBuilder.CreateDefault()
                        .AddService("true-mobile",
                            serviceVersion: "0.1.0") // the service name is also used for the Honeycomb dataset
                        .AddAttributes(new Dictionary<string, object>
                        {
                            ["deployment.environment"] = "Development", // builder.Environment.EnvironmentName,
                            ["service.instance.id"] = DeviceId,
                            ["device.model.name"] = DeviceName,
                            ["device.type"] = DeviceType,
                        }))
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                        options.EnrichWithHttpRequestMessage = (activity, request) =>
                        {
                            activity.DisplayName = $"{request.Method.Method} {request.RequestUri}";
                        };
                    })
                    .AddOtlpExporter(options =>
                    {
                        // Honeycomb configuration
                        options.Endpoint = new Uri(config["Honeycomb:Endpoint"]
                                                   ?? throw new InvalidOperationException("NULL Honeycomb:Endpoint configuration value"));
                        options.Protocol = OtlpExportProtocol.HttpProtobuf;
                        options.Headers = $"x-honeycomb-team={config["Honeycomb:ApiKey"]
                                                              ?? throw new InvalidOperationException("NULL Honeycomb:ApiKey configuration value")}";
                    })
#if DEBUG
                    .AddConsoleExporter()
#endif
                    ;
            });

        RegisterPlatformServices(services);

        Services = services.BuildServiceProvider();

        // no IHost in Avalonia — force OTel initialization
        Services.GetRequiredService<TracerProvider>();

        var viewModel = Services.GetRequiredService<MainViewModel>();

        switch (ApplicationLifetime)
        {
            case IClassicDesktopStyleApplicationLifetime desktop:
                desktop.MainWindow = new MainWindow
                {
                    DataContext = viewModel,
                };
                desktop.Exit += (_, _) => Services.Dispose();
                break;
            case ISingleViewApplicationLifetime singleViewPlatform:
                singleViewPlatform.MainView = new PageNavigationHost()
                {
                    Page = new MainView { DataContext = viewModel }
                };
                break;
            // case IActivityApplicationLifetime singleViewFactoryApplicationLifetime:
            //     singleViewFactoryApplicationLifetime.MainViewFactory = () => new PageNavigationHost()
            //     {
            //         Page = new MainView { DataContext = viewModel }
            //     };
            //     break;
        }

        base.OnFrameworkInitializationCompleted();
    }
}
