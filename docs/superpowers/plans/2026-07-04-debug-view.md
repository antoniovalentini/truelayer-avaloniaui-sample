# Debug View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-app "Debug" tab (Desktop + Android) for live troubleshooting: log/crash viewer, network inspector, auth/deep-link tracer, token inspector, device info, telemetry status, storage inspector, and a share/export action — per `docs/superpowers/specs/2026-07-04-debug-view-design.md`.

**Architecture:** New bounded in-memory stores (`IDebugLogStore`, `IDebugNetworkStore`, `IDebugTelemetryStatus`) are fed passively by hooking into logging/HTTP/OTel infrastructure already wired in `App.axaml.cs`. A new `DebugViewModel` renders a menu of 7 sub-screens using the app's existing `TransitioningContentControl` + `ViewLocator` navigation idiom, one level below the top-level `TabbedPage`. Each sub-screen is an independent `UserControl`/`ViewModel` pair matching the existing `DataView`/`PaymentView`/`SettingsView` style.

**Tech Stack:** Avalonia 12 (`TabbedPage`, `ContentPage`, `TransitioningContentControl`), CommunityToolkit.Mvvm 8.4, Microsoft.Extensions.Logging/DependencyInjection/Http 10.x, xunit (new test project).

## Global Constraints

- Target framework: `net10.0` (Desktop/shared), `net10.0-android` (Android) — matches existing csproj files.
- No new third-party dependency for navigation — reuse `ViewLocator` + `Avalonia.Controls.TransitioningContentControl` (confirmed present in `Avalonia.Controls.dll` 12.0.5, already referenced).
- Debug tab is visible in both Debug and Release builds — no `#if DEBUG` gating on the feature itself.
- `IDebugLogStore` capacity: 500 entries. `IDebugNetworkStore` capacity: 100 entries. Both fixed, not configurable, oldest-first eviction.
- The shared diagnostics export bundle never includes token/secret values, masked or otherwise.
- Do not modify `libs/truelayer-dotnet-data` (vendored submodule) — `IApiClient`/`ApiClient` there are `internal`, so the network inspector attaches via `services.ConfigureHttpClientDefaults(...)`, not by referencing those types.
- Central package versions live in `src/Directory.Packages.props` (`ManagePackageVersionsCentrally=true`). Any new `<PackageReference>` must omit `Version` and get a matching `<PackageVersion>` entry there instead.
- Out of scope (per spec §12): dev-only build gating, cross-restart persistence, remote log shipping, pluggable sinks, `AndroidManifest`/`FileProvider` changes. Also out of scope for this plan: XAML-previewer `Design.DataContext` sample data for the 8 new views (existing convention, skipped here as pure DX polish with no functional effect — add later if the previewer experience matters).

---

## Task 1: Test project bootstrap + `BoundedBuffer<T>`

**Files:**
- Create: `src/MobileApp.Tests/MobileApp.Tests.csproj`
- Create: `src/MobileApp.Tests/BoundedBufferTests.cs`
- Create: `src/MobileApp/Debug/BoundedBuffer.cs`
- Modify: `src/Directory.Packages.props`
- Modify: `src/MobileApp/MobileApp.csproj`
- Modify: `.github/workflows/release.yml`

**Interfaces:**
- Produces: `MobileApp.Debug.BoundedBuffer<T>` — `internal sealed class` with `void Add(T item)` and `IReadOnlyList<T> Snapshot()`. Used by Task 2 (`DebugLogStore`) and Task 3 (`DebugNetworkStore`).

- [ ] **Step 1: Add test package versions to central package management**

Edit `src/Directory.Packages.props`, add inside the existing `<ItemGroup>` (after the last `<PackageVersion>` line, before `</ItemGroup>`):

```xml
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageVersion Include="xunit" Version="2.9.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="2.8.2" />
```

- [ ] **Step 2: Create the test project**

Create `src/MobileApp.Tests/MobileApp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\MobileApp\MobileApp.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add the project to the solution**

Run: `dotnet sln truelayer-samples.sln add src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: `Project ... added to the solution.`

- [ ] **Step 4: Allow the test project to see `internal` types in MobileApp**

Edit `src/MobileApp/MobileApp.csproj`, add a new `<ItemGroup>` before the closing `</Project>` tag:

```xml
  <ItemGroup>
    <InternalsVisibleTo Include="MobileApp.Tests" />
  </ItemGroup>
```

- [ ] **Step 5: Write the failing test for `BoundedBuffer<T>`**

Create `src/MobileApp.Tests/BoundedBufferTests.cs`:

```csharp
using MobileApp.Debug;
using Xunit;

namespace MobileApp.Tests;

public class BoundedBufferTests
{
    [Fact]
    public void Add_WithinCapacity_ReturnsAllItemsInOrder()
    {
        var buffer = new BoundedBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);

        Assert.Equal(new[] { 1, 2 }, buffer.Snapshot());
    }

    [Fact]
    public void Add_BeyondCapacity_EvictsOldestFirst()
    {
        var buffer = new BoundedBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal(new[] { 2, 3, 4 }, buffer.Snapshot());
    }
}
```

- [ ] **Step 6: Run the test to verify it fails**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: FAIL to build — `BoundedBuffer<>` does not exist yet.

- [ ] **Step 7: Implement `BoundedBuffer<T>`**

Create `src/MobileApp/Debug/BoundedBuffer.cs`:

```csharp
using System.Collections.Generic;

namespace MobileApp.Debug;

internal sealed class BoundedBuffer<T>(int capacity)
{
    private readonly object _lock = new();
    private readonly Queue<T> _items = new(capacity);

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_items.Count == capacity)
            {
                _items.Dequeue();
            }
            _items.Enqueue(item);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            return _items.ToArray();
        }
    }
}
```

- [ ] **Step 8: Run the test to verify it passes**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: PASS (2 tests).

- [ ] **Step 9: Wire the test project into CI**

Edit `.github/workflows/release.yml`, insert a new step right after the `Install Android workload` step and before `Build Desktop`:

```yaml
      - name: Run tests
        run: dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj -c Release
```

- [ ] **Step 10: Commit**

```bash
git add src/MobileApp.Tests src/Directory.Packages.props src/MobileApp/MobileApp.csproj truelayer-samples.sln .github/workflows/release.yml
git commit -m "Add MobileApp.Tests project with BoundedBuffer<T>"
```

---

## Task 2: Log store + in-memory logger provider + crash capture

**Files:**
- Create: `src/MobileApp/Debug/DebugLogEntry.cs`
- Create: `src/MobileApp/Debug/IDebugLogStore.cs`
- Create: `src/MobileApp/Debug/InMemoryLoggerProvider.cs`
- Modify: `src/MobileApp/App.axaml.cs`

**Interfaces:**
- Consumes: `BoundedBuffer<T>` (Task 1).
- Produces: `MobileApp.Debug.DebugLogEntry` record `(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception)`; `MobileApp.Debug.IDebugLogStore` with `void Add(DebugLogEntry entry)` and `IReadOnlyList<DebugLogEntry> Snapshot()`; `MobileApp.Debug.InMemoryLoggerProvider`. These feed the Auth/Deep-link tracer (Task 5), the Logs screen (Task 9), the Auth-logs screen (Task 10), and the export bundle (Task 15).

- [ ] **Step 1: Create `DebugLogEntry`**

Create `src/MobileApp/Debug/DebugLogEntry.cs`:

```csharp
using System;
using Microsoft.Extensions.Logging;

namespace MobileApp.Debug;

public sealed record DebugLogEntry(DateTimeOffset Timestamp, LogLevel Level, string Category, string Message, string? Exception);
```

- [ ] **Step 2: Create `IDebugLogStore`/`DebugLogStore`**

Create `src/MobileApp/Debug/IDebugLogStore.cs`:

```csharp
using System.Collections.Generic;

namespace MobileApp.Debug;

public interface IDebugLogStore
{
    void Add(DebugLogEntry entry);
    IReadOnlyList<DebugLogEntry> Snapshot();
}

public sealed class DebugLogStore : IDebugLogStore
{
    private const int Capacity = 500;
    private readonly BoundedBuffer<DebugLogEntry> _buffer = new(Capacity);

    public void Add(DebugLogEntry entry) => _buffer.Add(entry);

    public IReadOnlyList<DebugLogEntry> Snapshot() => _buffer.Snapshot();
}
```

- [ ] **Step 3: Create `InMemoryLoggerProvider`**

Create `src/MobileApp/Debug/InMemoryLoggerProvider.cs`:

```csharp
using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace MobileApp.Debug;

public sealed class InMemoryLoggerProvider(IDebugLogStore store) : ILoggerProvider
{
    private readonly ConcurrentDictionary<string, InMemoryLogger> _loggers = new();

    public ILogger CreateLogger(string categoryName) =>
        _loggers.GetOrAdd(categoryName, name => new InMemoryLogger(name, store));

    public void Dispose() => _loggers.Clear();

    private sealed class InMemoryLogger(string category, IDebugLogStore store) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            try
            {
                var message = formatter(state, exception);
                store.Add(new DebugLogEntry(DateTimeOffset.UtcNow, logLevel, category, message, exception?.ToString()));
            }
            catch
            {
                // Log capture must never break the real logging pipeline.
            }
        }
    }
}
```

- [ ] **Step 4: Wire the store and provider into `App.axaml.cs`, and hook crash capture**

In `src/MobileApp/App.axaml.cs`, add these usings after the existing `using MobileApp.Views;` line:

```csharp
using MobileApp.Debug;
```

Replace this block (currently around line 66-90):

```csharp
        var configBuilder = new ConfigurationBuilder();

        PlatformConfiguration(configBuilder);

        var config = configBuilder.Build();

        var services = new ServiceCollection();
        services
            .AddLogging(builder => builder.AddConsole())
            .AddSingleton<MainViewModel>()
```

with:

```csharp
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
```

This requires `using Microsoft.Extensions.Logging;` — already present in `App.axaml.cs` (used for `IServiceCollection` logging extension methods via `Microsoft.Extensions.DependencyInjection`); confirm `LogLevel`/`ILogger` resolve — if the build reports `LogLevel` unresolved, add `using Microsoft.Extensions.Logging;` at the top of the file.

- [ ] **Step 5: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/MobileApp/Debug src/MobileApp/App.axaml.cs
git commit -m "Add in-memory log store, logger provider, and crash capture"
```

---

## Task 3: Network store + HTTP logging handler

**Files:**
- Create: `src/MobileApp/Debug/DebugNetworkEntry.cs`
- Create: `src/MobileApp/Debug/IDebugNetworkStore.cs`
- Create: `src/MobileApp/Debug/DebugHttpLoggingHandler.cs`
- Modify: `src/MobileApp/App.axaml.cs`

**Interfaces:**
- Consumes: `BoundedBuffer<T>` (Task 1).
- Produces: `MobileApp.Debug.DebugNetworkEntry` record; `MobileApp.Debug.IDebugNetworkStore` with `void Add(DebugNetworkEntry entry)` / `IReadOnlyList<DebugNetworkEntry> Snapshot()`. Consumed by the Network screen (Task 11) and the export bundle (Task 15).

**Note on approach:** The TrueLayer SDK registers its `HttpClient` via `services.AddHttpClient<IApiClient, ApiClient>()` in `libs/truelayer-dotnet-data/src/TrueLayer/TrueLayerServiceCollectionExtensions.cs:43`, but both `IApiClient` and `ApiClient` are declared `internal` to that assembly — they cannot be referenced from `MobileApp`. Since this app has exactly one `HttpClient` created via `IHttpClientFactory` (TrueLayer's), `services.ConfigureHttpClientDefaults(...)` applies the handler to it without needing to name or type the client, and without touching the vendored submodule.

- [ ] **Step 1: Create `DebugNetworkEntry`**

Create `src/MobileApp/Debug/DebugNetworkEntry.cs`:

```csharp
using System;

namespace MobileApp.Debug;

public sealed record DebugNetworkEntry(
    DateTimeOffset Timestamp,
    string Method,
    string Uri,
    int? StatusCode,
    long DurationMs,
    string? TraceId,
    string? Error);
```

- [ ] **Step 2: Create `IDebugNetworkStore`/`DebugNetworkStore`**

Create `src/MobileApp/Debug/IDebugNetworkStore.cs`:

```csharp
using System.Collections.Generic;

namespace MobileApp.Debug;

public interface IDebugNetworkStore
{
    void Add(DebugNetworkEntry entry);
    IReadOnlyList<DebugNetworkEntry> Snapshot();
}

public sealed class DebugNetworkStore : IDebugNetworkStore
{
    private const int Capacity = 100;
    private readonly BoundedBuffer<DebugNetworkEntry> _buffer = new(Capacity);

    public void Add(DebugNetworkEntry entry) => _buffer.Add(entry);

    public IReadOnlyList<DebugNetworkEntry> Snapshot() => _buffer.Snapshot();
}
```

- [ ] **Step 3: Create `DebugHttpLoggingHandler`**

Create `src/MobileApp/Debug/DebugHttpLoggingHandler.cs`:

```csharp
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
```

- [ ] **Step 4: Register the store and handler in `App.axaml.cs`**

In `src/MobileApp/App.axaml.cs`, in the same `services` chain from Task 2, add `.AddSingleton<IDebugNetworkStore, DebugNetworkStore>()` and `.AddTransient<DebugHttpLoggingHandler>()` anywhere in the fluent chain (e.g. right after `.AddSingleton<IDebugLogStore>(debugLogStore)`), and after the full `services` chain statement (after the closing `;` of the `.AddTrueLayer(...)` call), add:

```csharp
        services.ConfigureHttpClientDefaults(builder => builder.AddHttpMessageHandler<DebugHttpLoggingHandler>());
```

- [ ] **Step 5: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/MobileApp/Debug src/MobileApp/App.axaml.cs
git commit -m "Add network call inspector store and HTTP logging handler"
```

---

## Task 4: Telemetry status tracking

**Files:**
- Create: `src/MobileApp/Debug/IDebugTelemetryStatus.cs`
- Modify: `src/MobileApp/OtelDiagnosticsListener.cs`
- Modify: `src/MobileApp/App.axaml.cs`

**Interfaces:**
- Produces: `MobileApp.Debug.TelemetryStatusSnapshot` record `(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? LastFailureMessage)`; `MobileApp.Debug.IDebugTelemetryStatus` with `TelemetryStatusSnapshot Snapshot()`, `void RecordSuccess(DateTimeOffset timestamp)`, `void RecordFailure(DateTimeOffset timestamp, string message)`. Consumed by the Telemetry screen (Task 13) and export bundle (Task 15).
- Also registers `IConfiguration` in DI (previously only a local variable), needed by the Telemetry screen to read the configured Honeycomb endpoint.

**Grounding:** Decompiling `OpenTelemetry.Exporter.OpenTelemetryProtocol.dll` (1.16.0) confirms `OpenTelemetryProtocolExporterEventSource` fires `ExportSuccess` (EventId 21, Informational) on successful export and `ExportFailure` (EventId 23, Error) on failure — these are the exact `EventWrittenEventArgs.EventName` values to match on.

- [ ] **Step 1: Create `IDebugTelemetryStatus`**

Create `src/MobileApp/Debug/IDebugTelemetryStatus.cs`:

```csharp
using System;

namespace MobileApp.Debug;

public sealed record TelemetryStatusSnapshot(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? LastFailureMessage);

public interface IDebugTelemetryStatus
{
    TelemetryStatusSnapshot Snapshot();
    void RecordSuccess(DateTimeOffset timestamp);
    void RecordFailure(DateTimeOffset timestamp, string message);
}

public sealed class DebugTelemetryStatus : IDebugTelemetryStatus
{
    private readonly object _lock = new();
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastFailureAt;
    private string? _lastFailureMessage;

    public TelemetryStatusSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new TelemetryStatusSnapshot(_lastSuccessAt, _lastFailureAt, _lastFailureMessage);
        }
    }

    public void RecordSuccess(DateTimeOffset timestamp)
    {
        lock (_lock) { _lastSuccessAt = timestamp; }
    }

    public void RecordFailure(DateTimeOffset timestamp, string message)
    {
        lock (_lock)
        {
            _lastFailureAt = timestamp;
            _lastFailureMessage = message;
        }
    }
}
```

- [ ] **Step 2: Extend `OtelDiagnosticsListener` to record status**

Replace the full contents of `src/MobileApp/OtelDiagnosticsListener.cs` with:

```csharp
using System;
using System.Diagnostics.Tracing;
using MobileApp.Debug;

namespace MobileApp;

internal sealed class OtelDiagnosticsListener(IDebugTelemetryStatus telemetryStatus) : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name.StartsWith("OpenTelemetry", StringComparison.Ordinal))
            EnableEvents(source, EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        string msg;
        try
        {
            msg = e.Payload is { Count: > 0 }
                ? string.Format(e.Message ?? string.Empty, [.. e.Payload])
                : e.Message ?? e.EventName ?? "(no message)";
        }
        catch (FormatException)
        {
            msg = e.Message ?? e.EventName ?? "(no message)";
        }
        Console.WriteLine($"[OTel/{e.Level}] {msg}");

        switch (e.EventName)
        {
            case "ExportSuccess":
                telemetryStatus.RecordSuccess(DateTimeOffset.UtcNow);
                break;
            case "ExportFailure":
                telemetryStatus.RecordFailure(DateTimeOffset.UtcNow, msg);
                break;
        }
    }
}
```

- [ ] **Step 3: Register `IDebugTelemetryStatus`/`IConfiguration`, and move listener creation after the container is built**

In `src/MobileApp/App.axaml.cs`:

1. Remove the `#if DEBUG` / `_ = new OtelDiagnosticsListener();` / `#endif` block (currently right after the `Design.IsDesignMode` early-return).
2. Add `.AddSingleton<IDebugTelemetryStatus, DebugTelemetryStatus>()` and `.AddSingleton<IConfiguration>(config)` to the `services` chain (same chain as Tasks 2/3).
3. Replace:

```csharp
        Services = services.BuildServiceProvider();

        // no IHost in Avalonia — force OTel initialization
        Services.GetRequiredService<TracerProvider>();
```

with:

```csharp
        Services = services.BuildServiceProvider();

        _ = new OtelDiagnosticsListener(Services.GetRequiredService<IDebugTelemetryStatus>());

        // no IHost in Avalonia — force OTel initialization
        Services.GetRequiredService<TracerProvider>();
```

- [ ] **Step 4: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add src/MobileApp/Debug src/MobileApp/OtelDiagnosticsListener.cs src/MobileApp/App.axaml.cs
git commit -m "Track OTel export success/failure and register IConfiguration"
```

---

## Task 5: Auth / deep-link tracer (log the existing redirect flow)

**Files:**
- Modify: `src/MobileApp/IAuthManager.cs`
- Modify: `src/MobileApp.Android/MainActivity.cs`
- Modify: `src/MobileApp.Android/AndroidRedirectManager.cs`
- Modify: `src/MobileApp.Desktop/DesktopRedirectManager.cs`

**Interfaces:**
- Consumes: `IDebugLogStore` indirectly (via `ILogger<T>` → `InMemoryLoggerProvider` from Task 2 — no direct dependency added here).
- Produces: nothing new; this task converts existing `Console.WriteLine` diagnostics (including a call site literally marked `// TODO: Find a way to use Logger<T> here`) into `ILogger` calls, so they flow into `IDebugLogStore` for free. The Auth-logs screen (Task 10) filters `IDebugLogStore` by these 4 category names: `typeof(AuthManager).FullName`, `"MobileApp.Android.MainActivity"`, `"MobileApp.Android.AndroidRedirectManager"`, `"MobileApp.Desktop.DesktopRedirectManager"`.

- [ ] **Step 1: Log the callback query params in `AuthManager`**

In `src/MobileApp/IAuthManager.cs`, in `AuthManager.DoStuff()`, insert a log line right before `CallbackReceived?.Invoke(...)` (currently the last line of the method):

```csharp
        _logger.LogInformation("Received auth callback with {ParamCount} query parameters", queryParams.Count);
        CallbackReceived?.Invoke(this, new CallbackReceivedEventArgs(queryParams));
```

- [ ] **Step 2: Replace `Console.WriteLine` with `ILogger` in `MainActivity.HandleIntent`**

In `src/MobileApp.Android/MainActivity.cs`, add `using Microsoft.Extensions.Logging;` to the usings, then replace the body of `HandleIntent` (currently using `Console.WriteLine` and marked with a `// TODO: Find a way to use Logger<T> here` comment) with:

```csharp
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
```

- [ ] **Step 3: Add logging to `AndroidRedirectManager`**

Replace `src/MobileApp.Android/AndroidRedirectManager.cs` contents:

```csharp
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
```

- [ ] **Step 4: Add logging to `DesktopRedirectManager`**

Replace `src/MobileApp.Desktop/DesktopRedirectManager.cs` contents:

```csharp
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
```

No DI registration changes are needed: `AndroidRedirectManager`/`DesktopRedirectManager` are already registered via `AddSingleton<IRedirectManager, ...>()` in each platform's `RegisterPlatformServices`, and `AddLogging(...)` (Task 2) already makes `ILogger<T>` injectable for any `T`.

- [ ] **Step 5: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

Run: `dotnet build src/MobileApp.Android/MobileApp.Android.csproj -p:AndroidSdkDirectory="<path-to-android-sdk>"`
Expected: Build succeeded. (Skip if no local Android SDK is configured — Task 16's final build check covers both platforms again.)

- [ ] **Step 6: Commit**

```bash
git add src/MobileApp/IAuthManager.cs src/MobileApp.Android/MainActivity.cs src/MobileApp.Android/AndroidRedirectManager.cs src/MobileApp.Desktop/DesktopRedirectManager.cs
git commit -m "Log auth redirect/deep-link flow via ILogger instead of Console.WriteLine"
```

---

## Task 6: Token expiry (`OAuthToken.IssuedAt`) + formatting helpers

**Files:**
- Modify: `src/MobileApp/IAuthTokenStorage.cs`
- Modify: `src/MobileApp/ViewModels/DataViewModel.cs`
- Create: `src/MobileApp/Debug/DebugTokenFormatting.cs`
- Create: `src/MobileApp.Tests/DebugTokenFormattingTests.cs`

**Interfaces:**
- Produces: `OAuthToken.IssuedAt` (new field, default `default(DateTimeOffset)`); `MobileApp.Debug.DebugTokenFormatting.GetExpiryStatus(OAuthToken token, DateTimeOffset now) : string`; `MobileApp.Debug.DebugTokenFormatting.MaskSecret(string value) : string`. Consumed by the Tokens screen (Task 12) and Storage screen (Task 14).

- [ ] **Step 1: Add `IssuedAt` to `OAuthToken`**

In `src/MobileApp/IAuthTokenStorage.cs`, replace the last line of the file:

```csharp
public record OAuthToken(string ProviderId, string AccessToken, string TokenType, long ExpiresIn, string RefreshToken);
```

with:

```csharp
public record OAuthToken(string ProviderId, string AccessToken, string TokenType, long ExpiresIn, string RefreshToken, DateTimeOffset IssuedAt = default);
```

This is source- and JSON-compatible: `DesignDataViewModel.cs` (positional, 5 args) still compiles via the default parameter, and existing stored/exported JSON without an `IssuedAt` field deserializes it to `default(DateTimeOffset)` (year 1) — the Tokens screen will show this as already-expired, which self-corrects on the next refresh.

- [ ] **Step 2: Set `IssuedAt` when a token is added or refreshed**

In `src/MobileApp/ViewModels/DataViewModel.cs`, in `AddTokenAsync`, replace:

```csharp
        Tokens.Add(new OAuthToken(
            providerId,
            response.AccessToken,
            response.TokenType,
            response.ExpiresIn,
            response.RefreshToken));
```

with:

```csharp
        Tokens.Add(new OAuthToken(
            providerId,
            response.AccessToken,
            response.TokenType,
            response.ExpiresIn,
            response.RefreshToken,
            DateTimeOffset.UtcNow));
```

- [ ] **Step 3: Write the failing tests for the formatting helpers**

Create `src/MobileApp.Tests/DebugTokenFormattingTests.cs`:

```csharp
using System;
using MobileApp.Debug;
using Xunit;

namespace MobileApp.Tests;

public class DebugTokenFormattingTests
{
    [Fact]
    public void GetExpiryStatus_WhenNotYetExpired_ReturnsExpiresInMessage()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh", issuedAt);
        var now = issuedAt.AddMinutes(30);

        var result = DebugTokenFormatting.GetExpiryStatus(token, now);

        Assert.Equal("Expires in 30m", result);
    }

    [Fact]
    public void GetExpiryStatus_WhenExpired_ReturnsExpiredMessage()
    {
        var issuedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh", issuedAt);
        var now = issuedAt.AddHours(2);

        var result = DebugTokenFormatting.GetExpiryStatus(token, now);

        Assert.Equal("Expired 1h 0m ago", result);
    }

    [Fact]
    public void GetExpiryStatus_WithDefaultIssuedAt_ReturnsExpired()
    {
        var token = new OAuthToken("provider", "access", "Bearer", 3600, "refresh");

        var result = DebugTokenFormatting.GetExpiryStatus(token, DateTimeOffset.UtcNow);

        Assert.StartsWith("Expired", result);
    }

    [Fact]
    public void MaskSecret_LongValue_KeepsLastFourCharacters()
    {
        var result = DebugTokenFormatting.MaskSecret("abcdef1234567890");

        Assert.Equal("••••••••7890", result);
    }

    [Fact]
    public void MaskSecret_ShortValue_FullyMasked()
    {
        var result = DebugTokenFormatting.MaskSecret("ab");

        Assert.Equal("••••••••", result);
    }
}
```

- [ ] **Step 4: Run tests to verify they fail**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: FAIL to build — `DebugTokenFormatting` does not exist yet.

- [ ] **Step 5: Implement `DebugTokenFormatting`**

Create `src/MobileApp/Debug/DebugTokenFormatting.cs`:

```csharp
using System;

namespace MobileApp.Debug;

public static class DebugTokenFormatting
{
    public static string GetExpiryStatus(OAuthToken token, DateTimeOffset now)
    {
        var expiresAt = token.IssuedAt + TimeSpan.FromSeconds(token.ExpiresIn);
        var remaining = expiresAt - now;

        return remaining <= TimeSpan.Zero
            ? $"Expired {FormatDuration(-remaining)} ago"
            : $"Expires in {FormatDuration(remaining)}";
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalHours >= 1
            ? $"{(int)duration.TotalHours}h {duration.Minutes}m"
            : $"{(int)duration.TotalMinutes}m";

    public static string MaskSecret(string value)
    {
        const int visibleSuffixLength = 4;
        const string mask = "••••••••";

        return string.IsNullOrEmpty(value) || value.Length <= visibleSuffixLength
            ? mask
            : mask + value[^visibleSuffixLength..];
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: PASS (all tests, including Task 1's).

- [ ] **Step 7: Commit**

```bash
git add src/MobileApp/IAuthTokenStorage.cs src/MobileApp/ViewModels/DataViewModel.cs src/MobileApp/Debug/DebugTokenFormatting.cs src/MobileApp.Tests/DebugTokenFormattingTests.cs
git commit -m "Add OAuthToken.IssuedAt and token expiry/masking helpers"
```

---

## Task 7: Storage inspector backing (`IAuthTokenStorage.InspectManagedFiles`)

**Files:**
- Modify: `src/MobileApp/IAuthTokenStorage.cs`

**Interfaces:**
- Produces: `MobileApp.StorageFileSnapshot` record `(string FileName, string FullPath, bool Exists, long SizeBytes, DateTimeOffset? LastModifiedUtc)`; `IAuthTokenStorage.InspectManagedFiles() : IReadOnlyList<StorageFileSnapshot>`. Consumed by the Storage screen (Task 14).

No automated test for this method per spec §11 (file I/O against a fixed OS special-folder path isn't part of the approved test scope) — verified manually via the Storage screen once built (Task 14).

- [ ] **Step 1: Add the interface method and record**

In `src/MobileApp/IAuthTokenStorage.cs`, add `InspectManagedFiles` to the `IAuthTokenStorage` interface (after `ImportSettings`):

```csharp
public interface IAuthTokenStorage
{
    OAuthToken[]? LoadTokens();
    Task StoreTokens(OAuthToken[] token);
    Task<T?> Load<T>(string fileName);
    Task Store<T>(string fileName, T blob);
    Task ExportSettings(Stream outputStream);
    Task ImportSettings(Stream inputStream);
    IReadOnlyList<StorageFileSnapshot> InspectManagedFiles();
}
```

Add the record next to `OAuthToken` at the bottom of the file:

```csharp
public sealed record StorageFileSnapshot(string FileName, string FullPath, bool Exists, long SizeBytes, DateTimeOffset? LastModifiedUtc);
```

- [ ] **Step 2: Implement it in `AuthTokenStorage`**

In the `AuthTokenStorage` class, add (e.g. after `ImportSettings`, before the private `SettingsBackup` record):

```csharp
    public IReadOnlyList<StorageFileSnapshot> InspectManagedFiles()
    {
        return
        [
            Inspect("settings.json", Path.Combine(SecretsFolderPath, "settings.json")),
            Inspect("beneficiaries.json", Path.Combine(BasePath, "beneficiaries.json")),
        ];

        static StorageFileSnapshot Inspect(string fileName, string fullPath)
        {
            var info = new FileInfo(fullPath);
            return info.Exists
                ? new StorageFileSnapshot(fileName, fullPath, true, info.Length, info.LastWriteTimeUtc)
                : new StorageFileSnapshot(fileName, fullPath, false, 0, null);
        }
    }
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/IAuthTokenStorage.cs
git commit -m "Add IAuthTokenStorage.InspectManagedFiles for the storage inspector"
```

---

## Task 8: Device info visibility

**Files:**
- Modify: `src/MobileApp/App.axaml.cs`

**Interfaces:**
- Produces: `App.DeviceId`, `App.DeviceName`, `App.DeviceType` widened from `protected virtual` to `public virtual`. Consumed by the Device Info screen (Task 13) and the export bundle (Task 15).

- [ ] **Step 1: Widen visibility**

In `src/MobileApp/App.axaml.cs`, replace:

```csharp
    protected virtual string DeviceId => Environment.MachineName;
    protected virtual string DeviceName => Environment.MachineName;
    protected virtual string DeviceType => Environment.OSVersion.ToString();
```

with:

```csharp
    public virtual string DeviceId => Environment.MachineName;
    public virtual string DeviceName => Environment.MachineName;
    public virtual string DeviceType => Environment.OSVersion.ToString();
```

`AndroidApp`'s overrides in `src/MobileApp.Android/MainActivity.cs` (`protected override string DeviceId => ...` etc.) must also change their access modifier to `public override` to match — update those three overrides accordingly.

- [ ] **Step 2: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add src/MobileApp/App.axaml.cs src/MobileApp.Android/MainActivity.cs
git commit -m "Expose device identity properties publicly for the debug device info screen"
```

---

## Task 9: Logs screen (`DebugLogsView`/`DebugLogsViewModel`)

**Files:**
- Create: `src/MobileApp/ViewModels/DebugLogsViewModel.cs`
- Create: `src/MobileApp/Views/DebugLogsView.axaml`
- Create: `src/MobileApp/Views/DebugLogsView.axaml.cs`

**Interfaces:**
- Consumes: `IDebugLogStore` (Task 2).
- Produces: `MobileApp.ViewModels.DebugLogsViewModel`, resolvable by `ViewLocator` as `MobileApp.Views.DebugLogsView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement the ViewModel**

Create `src/MobileApp/ViewModels/DebugLogsViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugLogsViewModel : ViewModelBase
{
    private readonly IDebugLogStore _store;

    public DebugLogsViewModel(IDebugLogStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    public LogLevel?[] AvailableLevels { get; } =
        [null, LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical];

    [ObservableProperty] private string? _searchText;
    [ObservableProperty] private LogLevel? _levelFilter;

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        var filtered = _store.Snapshot()
            .Where(e => LevelFilter is null || e.Level == LevelFilter)
            .Where(e => string.IsNullOrWhiteSpace(SearchText) || e.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
            .Reverse();
        Entries.AddRange(filtered);
    }

    partial void OnSearchTextChanged(string? value) => Refresh();
    partial void OnLevelFilterChanged(LogLevel? value) => Refresh();
}
```

- [ ] **Step 2: Implement the View**

Create `src/MobileApp/Views/DebugLogsView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugLogsView"
             x:DataType="vm:DebugLogsViewModel"
             Background="{DynamicResource Charcoal}">
  <ScrollViewer>
    <StackPanel Spacing="10">
      <Grid ColumnDefinitions="*,Auto">
        <TextBox Grid.Column="0" Text="{Binding SearchText}" Watermark="Search message..."/>
        <Button Grid.Column="1" Content="Refresh" Command="{Binding RefreshCommand}" Margin="10 0 0 0"
                Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      </Grid>
      <ComboBox ItemsSource="{Binding AvailableLevels}" SelectedItem="{Binding LevelFilter}"
                PlaceholderText="All levels" HorizontalAlignment="Stretch"/>
      <ItemsControl ItemsSource="{Binding Entries}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="8" Padding="10" Margin="0 0 0 8">
              <StackPanel Spacing="4">
                <TextBlock Text="{Binding Category}" FontWeight="SemiBold" FontSize="12"/>
                <TextBlock Text="{Binding Message}" TextWrapping="Wrap" FontSize="13"/>
                <TextBlock FontSize="11" Opacity="0.6">
                  <Run Text="{Binding Timestamp, StringFormat={}{0:HH:mm:ss}}"/>
                  <Run Text=" — "/>
                  <Run Text="{Binding Level}"/>
                </TextBlock>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/MobileApp/Views/DebugLogsView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugLogsView : UserControl
{
    public DebugLogsView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded. (The view isn't reachable from the UI yet — that's Task 16 — this only confirms it compiles.)

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/ViewModels/DebugLogsViewModel.cs src/MobileApp/Views/DebugLogsView.axaml src/MobileApp/Views/DebugLogsView.axaml.cs
git commit -m "Add debug logs screen"
```

---

## Task 10: Auth/deep-links screen (`DebugAuthLogsView`/`DebugAuthLogsViewModel`)

**Files:**
- Create: `src/MobileApp/ViewModels/DebugAuthLogsViewModel.cs`
- Create: `src/MobileApp/Views/DebugAuthLogsView.axaml`
- Create: `src/MobileApp/Views/DebugAuthLogsView.axaml.cs`

**Interfaces:**
- Consumes: `IDebugLogStore` (Task 2); relies on the category names logged in Task 5.
- Produces: `MobileApp.ViewModels.DebugAuthLogsViewModel` → `MobileApp.Views.DebugAuthLogsView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement the ViewModel**

Create `src/MobileApp/ViewModels/DebugAuthLogsViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugAuthLogsViewModel : ViewModelBase
{
    // MobileApp.Android/.Desktop types can't be referenced from this shared project
    // (dependency runs the other way), so their ILogger<T> category names
    // (== typeof(T).FullName) are hardcoded here.
    private static readonly HashSet<string> AuthCategories =
    [
        typeof(AuthManager).FullName!,
        "MobileApp.Android.MainActivity",
        "MobileApp.Android.AndroidRedirectManager",
        "MobileApp.Desktop.DesktopRedirectManager",
    ];

    private readonly IDebugLogStore _store;

    public DebugAuthLogsViewModel(IDebugLogStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugLogEntry> Entries { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        Entries.AddRange(_store.Snapshot().Where(e => AuthCategories.Contains(e.Category)).Reverse());
    }
}
```

- [ ] **Step 2: Implement the View**

Create `src/MobileApp/Views/DebugAuthLogsView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugAuthLogsView"
             x:DataType="vm:DebugAuthLogsViewModel"
             Background="{DynamicResource Charcoal}">
  <ScrollViewer>
    <StackPanel Spacing="10">
      <Button Content="Refresh" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"
              Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <ItemsControl ItemsSource="{Binding Entries}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="8" Padding="10" Margin="0 0 0 8">
              <StackPanel Spacing="4">
                <TextBlock Text="{Binding Category}" FontWeight="SemiBold" FontSize="12"/>
                <TextBlock Text="{Binding Message}" TextWrapping="Wrap" FontSize="13"/>
                <TextBlock Text="{Binding Timestamp, StringFormat={}{0:HH:mm:ss}}" FontSize="11" Opacity="0.6"/>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/MobileApp/Views/DebugAuthLogsView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugAuthLogsView : UserControl
{
    public DebugAuthLogsView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/ViewModels/DebugAuthLogsViewModel.cs src/MobileApp/Views/DebugAuthLogsView.axaml src/MobileApp/Views/DebugAuthLogsView.axaml.cs
git commit -m "Add auth/deep-link tracer screen"
```

---

## Task 11: Network screen (`DebugNetworkView`/`DebugNetworkViewModel`)

**Files:**
- Create: `src/MobileApp/ViewModels/DebugNetworkViewModel.cs`
- Create: `src/MobileApp/Views/DebugNetworkView.axaml`
- Create: `src/MobileApp/Views/DebugNetworkView.axaml.cs`

**Interfaces:**
- Consumes: `IDebugNetworkStore` (Task 3).
- Produces: `MobileApp.ViewModels.DebugNetworkViewModel` → `MobileApp.Views.DebugNetworkView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement the ViewModel**

Create `src/MobileApp/ViewModels/DebugNetworkViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugNetworkViewModel : ViewModelBase
{
    private readonly IDebugNetworkStore _store;

    public DebugNetworkViewModel(IDebugNetworkStore store)
    {
        _store = store;
        Refresh();
    }

    public ObservableCollection<DebugNetworkEntry> Entries { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Entries.Clear();
        Entries.AddRange(_store.Snapshot().Reverse());
    }
}
```

- [ ] **Step 2: Implement the View**

Create `src/MobileApp/Views/DebugNetworkView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugNetworkView"
             x:DataType="vm:DebugNetworkViewModel"
             Background="{DynamicResource Charcoal}">
  <ScrollViewer>
    <StackPanel Spacing="10">
      <Button Content="Refresh" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"
              Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <ItemsControl ItemsSource="{Binding Entries}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="8" Padding="10" Margin="0 0 0 8">
              <StackPanel Spacing="4">
                <TextBlock FontWeight="SemiBold" FontSize="12">
                  <Run Text="{Binding Method}"/>
                  <Run Text=" "/>
                  <Run Text="{Binding Uri}"/>
                </TextBlock>
                <TextBlock FontSize="12">
                  <Run Text="Status: "/>
                  <Run Text="{Binding StatusCode}"/>
                  <Run Text=" — "/>
                  <Run Text="{Binding DurationMs}"/>
                  <Run Text="ms"/>
                </TextBlock>
                <TextBlock Text="{Binding Error}" FontSize="11" Foreground="#DC2626" TextWrapping="Wrap"
                           IsVisible="{Binding Error, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/MobileApp/Views/DebugNetworkView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugNetworkView : UserControl
{
    public DebugNetworkView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/ViewModels/DebugNetworkViewModel.cs src/MobileApp/Views/DebugNetworkView.axaml src/MobileApp/Views/DebugNetworkView.axaml.cs
git commit -m "Add network inspector screen"
```

---

## Task 12: Tokens screen (`DebugTokensView`/`DebugTokensViewModel`)

**Files:**
- Create: `src/MobileApp/ViewModels/DebugTokensViewModel.cs`
- Create: `src/MobileApp/Views/DebugTokensView.axaml`
- Create: `src/MobileApp/Views/DebugTokensView.axaml.cs`

**Interfaces:**
- Consumes: `IAuthTokenStorage.LoadTokens()` (existing); `DebugTokenFormatting.GetExpiryStatus`/`MaskSecret` (Task 6).
- Produces: `MobileApp.ViewModels.DebugTokensViewModel` (and its item type `DebugTokenDisplay`) → `MobileApp.Views.DebugTokensView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement the ViewModel and its reveal-on-tap item type**

Create `src/MobileApp/ViewModels/DebugTokensViewModel.cs`:

```csharp
using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugTokensViewModel : ViewModelBase
{
    private readonly IAuthTokenStorage _storage;

    public DebugTokensViewModel(IAuthTokenStorage storage)
    {
        _storage = storage;
        Refresh();
    }

    public ObservableCollection<DebugTokenDisplay> Tokens { get; } = [];

    [RelayCommand]
    private void Refresh()
    {
        Tokens.Clear();
        var tokens = _storage.LoadTokens() ?? [];
        var now = DateTimeOffset.UtcNow;
        Tokens.AddRange(tokens.Select(t => new DebugTokenDisplay(
            t.ProviderId,
            DebugTokenFormatting.GetExpiryStatus(t, now),
            t.AccessToken,
            t.RefreshToken)));
    }
}

public sealed partial class DebugTokenDisplay : ObservableObject
{
    private readonly string _accessToken;
    private readonly string _refreshToken;

    public DebugTokenDisplay(string providerId, string expiryStatus, string accessToken, string refreshToken)
    {
        ProviderId = providerId;
        ExpiryStatus = expiryStatus;
        _accessToken = accessToken;
        _refreshToken = refreshToken;
    }

    public string ProviderId { get; }
    public string ExpiryStatus { get; }

    [ObservableProperty] private bool _isRevealed;

    public string AccessTokenDisplay => IsRevealed ? _accessToken : DebugTokenFormatting.MaskSecret(_accessToken);
    public string RefreshTokenDisplay => IsRevealed ? _refreshToken : DebugTokenFormatting.MaskSecret(_refreshToken);

    partial void OnIsRevealedChanged(bool value)
    {
        OnPropertyChanged(nameof(AccessTokenDisplay));
        OnPropertyChanged(nameof(RefreshTokenDisplay));
    }

    [RelayCommand]
    private void ToggleReveal() => IsRevealed = !IsRevealed;
}
```

- [ ] **Step 2: Implement the View**

Create `src/MobileApp/Views/DebugTokensView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugTokensView"
             x:DataType="vm:DebugTokensViewModel"
             Background="{DynamicResource Charcoal}">
  <ScrollViewer>
    <StackPanel Spacing="10">
      <Button Content="Refresh" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"
              Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <ItemsControl ItemsSource="{Binding Tokens}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="8" Padding="10" Margin="0 0 0 8">
              <StackPanel Spacing="4">
                <TextBlock Text="{Binding ProviderId}" FontWeight="SemiBold"/>
                <TextBlock Text="{Binding ExpiryStatus}" FontSize="12"/>
                <Button Content="{Binding AccessTokenDisplay, StringFormat='Access: {0}'}"
                        Command="{Binding ToggleRevealCommand}" Background="Transparent" BorderThickness="0"
                        HorizontalContentAlignment="Left" FontFamily="monospace" FontSize="11"/>
                <TextBlock Text="{Binding RefreshTokenDisplay, StringFormat='Refresh: {0}'}" FontFamily="monospace" FontSize="11"/>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/MobileApp/Views/DebugTokensView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugTokensView : UserControl
{
    public DebugTokensView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/ViewModels/DebugTokensViewModel.cs src/MobileApp/Views/DebugTokensView.axaml src/MobileApp/Views/DebugTokensView.axaml.cs
git commit -m "Add tokens screen with reveal-on-tap masking"
```

---

## Task 13: Device Info and Telemetry screens

**Files:**
- Create: `src/MobileApp/ViewModels/DebugDeviceInfoViewModel.cs`
- Create: `src/MobileApp/Views/DebugDeviceInfoView.axaml`
- Create: `src/MobileApp/Views/DebugDeviceInfoView.axaml.cs`
- Create: `src/MobileApp/ViewModels/DebugTelemetryViewModel.cs`
- Create: `src/MobileApp/Views/DebugTelemetryView.axaml`
- Create: `src/MobileApp/Views/DebugTelemetryView.axaml.cs`

**Interfaces:**
- Consumes: `App.Instance.{AppVersion,DeviceId,DeviceName,DeviceType}` (Task 8); `IDebugTelemetryStatus`, `IConfiguration` (Task 4).
- Produces: `MobileApp.ViewModels.DebugDeviceInfoViewModel` → `DebugDeviceInfoView`; `MobileApp.ViewModels.DebugTelemetryViewModel` → `DebugTelemetryView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement `DebugDeviceInfoViewModel`**

Create `src/MobileApp/ViewModels/DebugDeviceInfoViewModel.cs`:

```csharp
using System;

namespace MobileApp.ViewModels;

public partial class DebugDeviceInfoViewModel : ViewModelBase
{
    public string AppVersion { get; } = App.Instance.AppVersion;
    public string DeviceId { get; } = App.Instance.DeviceId;
    public string DeviceName { get; } = App.Instance.DeviceName;
    public string DeviceType { get; } = App.Instance.DeviceType;
    public string RuntimeVersion { get; } = Environment.Version.ToString();
}
```

- [ ] **Step 2: Implement `DebugDeviceInfoView`**

Create `src/MobileApp/Views/DebugDeviceInfoView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugDeviceInfoView"
             x:DataType="vm:DebugDeviceInfoViewModel"
             Background="{DynamicResource Charcoal}">
  <StackPanel Spacing="10">
    <TextBlock Text="{Binding AppVersion, StringFormat='App version: {0}'}" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="{Binding DeviceId, StringFormat='Device ID: {0}'}" Foreground="{DynamicResource PureWhite}" TextWrapping="Wrap"/>
    <TextBlock Text="{Binding DeviceName, StringFormat='Device name: {0}'}" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="{Binding DeviceType, StringFormat='Device type: {0}'}" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="{Binding RuntimeVersion, StringFormat='.NET runtime: {0}'}" Foreground="{DynamicResource PureWhite}"/>
  </StackPanel>
</UserControl>
```

Create `src/MobileApp/Views/DebugDeviceInfoView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugDeviceInfoView : UserControl
{
    public DebugDeviceInfoView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Implement `DebugTelemetryViewModel`**

Create `src/MobileApp/ViewModels/DebugTelemetryViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugTelemetryViewModel : ViewModelBase
{
    private readonly IDebugTelemetryStatus _status;

    public DebugTelemetryViewModel(IDebugTelemetryStatus status, IConfiguration configuration)
    {
        _status = status;
        HoneycombEndpoint = configuration["Honeycomb:Endpoint"] ?? "(not configured)";
        Refresh();
    }

    public string HoneycombEndpoint { get; }

    [ObservableProperty] private string _lastSuccessDisplay = "none";
    [ObservableProperty] private string _lastFailureDisplay = "none";

    [RelayCommand]
    private void Refresh()
    {
        var snapshot = _status.Snapshot();
        LastSuccessDisplay = snapshot.LastSuccessAt?.ToString("u") ?? "none";
        LastFailureDisplay = snapshot.LastFailureAt is { } failedAt
            ? $"{failedAt:u} — {snapshot.LastFailureMessage}"
            : "none";
    }
}
```

- [ ] **Step 4: Implement `DebugTelemetryView`**

Create `src/MobileApp/Views/DebugTelemetryView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugTelemetryView"
             x:DataType="vm:DebugTelemetryViewModel"
             Background="{DynamicResource Charcoal}">
  <StackPanel Spacing="10">
    <Button Content="Refresh" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"
            Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
    <TextBlock Text="{Binding HoneycombEndpoint, StringFormat='Endpoint: {0}'}" Foreground="{DynamicResource PureWhite}" TextWrapping="Wrap"/>
    <TextBlock Text="{Binding LastSuccessDisplay, StringFormat='Last success: {0}'}" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="{Binding LastFailureDisplay, StringFormat='Last failure: {0}'}" Foreground="{DynamicResource PureWhite}" TextWrapping="Wrap"/>
  </StackPanel>
</UserControl>
```

Create `src/MobileApp/Views/DebugTelemetryView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugTelemetryView : UserControl
{
    public DebugTelemetryView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 5: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/MobileApp/ViewModels/DebugDeviceInfoViewModel.cs src/MobileApp/Views/DebugDeviceInfoView.axaml src/MobileApp/Views/DebugDeviceInfoView.axaml.cs src/MobileApp/ViewModels/DebugTelemetryViewModel.cs src/MobileApp/Views/DebugTelemetryView.axaml src/MobileApp/Views/DebugTelemetryView.axaml.cs
git commit -m "Add device info and telemetry status screens"
```

---

## Task 14: Storage screen (`DebugStorageView`/`DebugStorageViewModel`)

**Files:**
- Create: `src/MobileApp/ViewModels/DebugStorageViewModel.cs`
- Create: `src/MobileApp/Views/DebugStorageView.axaml`
- Create: `src/MobileApp/Views/DebugStorageView.axaml.cs`

**Interfaces:**
- Consumes: `IAuthTokenStorage.InspectManagedFiles()` (Task 7), `.LoadTokens()`, `.Load<T>()` (existing); `DebugTokenFormatting.MaskSecret` (Task 6).
- Produces: `MobileApp.ViewModels.DebugStorageViewModel` → `MobileApp.Views.DebugStorageView`. Wired into the Debug menu in Task 16.

- [ ] **Step 1: Implement the ViewModel**

Create `src/MobileApp/ViewModels/DebugStorageViewModel.cs`:

```csharp
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;
using MobileApp.Models;

namespace MobileApp.ViewModels;

public partial class DebugStorageViewModel : ViewModelBase
{
    private readonly IAuthTokenStorage _storage;
    private static readonly JsonSerializerOptions PrettyPrint = new() { WriteIndented = true };

    public DebugStorageViewModel(IAuthTokenStorage storage)
    {
        _storage = storage;
        Refresh();
    }

    public ObservableCollection<StorageFileSnapshot> Files { get; } = [];

    [ObservableProperty] private string? _previewContent;

    [RelayCommand]
    private void Refresh()
    {
        Files.Clear();
        Files.AddRange(_storage.InspectManagedFiles());
    }

    [RelayCommand]
    private async Task PreviewAsync(StorageFileSnapshot file)
    {
        if (!file.Exists)
        {
            PreviewContent = "(file not found)";
            return;
        }

        PreviewContent = file.FileName switch
        {
            "settings.json" => BuildMaskedTokensPreview(),
            "beneficiaries.json" => await BuildBeneficiariesPreviewAsync(),
            _ => "(no preview available)",
        };
    }

    private string BuildMaskedTokensPreview()
    {
        var tokens = _storage.LoadTokens() ?? [];
        var masked = tokens.Select(t => new
        {
            t.ProviderId,
            AccessToken = DebugTokenFormatting.MaskSecret(t.AccessToken),
            RefreshToken = DebugTokenFormatting.MaskSecret(t.RefreshToken),
            t.TokenType,
            t.ExpiresIn,
            t.IssuedAt,
        }).ToList();
        return JsonSerializer.Serialize(masked, PrettyPrint);
    }

    private async Task<string> BuildBeneficiariesPreviewAsync()
    {
        var beneficiaries = await _storage.Load<List<BeneficiaryModel>>("beneficiaries.json") ?? [];
        return JsonSerializer.Serialize(beneficiaries, PrettyPrint);
    }
}
```

- [ ] **Step 2: Implement the View**

Create `src/MobileApp/Views/DebugStorageView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugStorageView"
             x:DataType="vm:DebugStorageViewModel"
             Background="{DynamicResource Charcoal}">
  <ScrollViewer>
    <StackPanel Spacing="10">
      <Button Content="Refresh" Command="{Binding RefreshCommand}" HorizontalAlignment="Left"
              Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <ItemsControl ItemsSource="{Binding Files}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="8" Padding="10" Margin="0 0 0 8">
              <StackPanel Spacing="4">
                <TextBlock Text="{Binding FileName}" FontWeight="SemiBold"/>
                <TextBlock Text="{Binding FullPath}" FontSize="11" TextWrapping="Wrap" Opacity="0.7"/>
                <TextBlock Text="{Binding SizeBytes, StringFormat='{}{0} bytes'}" FontSize="12"/>
                <Button Content="View content"
                        Command="{Binding $parent[ItemsControl].((vm:DebugStorageViewModel)DataContext).PreviewCommand}"
                        CommandParameter="{Binding}"
                        Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"
                        HorizontalAlignment="Left"/>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
      <TextBox Text="{Binding PreviewContent}" IsReadOnly="True" AcceptsReturn="True"
               FontFamily="monospace" FontSize="11" Height="200"
               IsVisible="{Binding PreviewContent, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
    </StackPanel>
  </ScrollViewer>
</UserControl>
```

Create `src/MobileApp/Views/DebugStorageView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DebugStorageView : UserControl
{
    public DebugStorageView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/ViewModels/DebugStorageViewModel.cs src/MobileApp/Views/DebugStorageView.axaml src/MobileApp/Views/DebugStorageView.axaml.cs
git commit -m "Add storage inspector screen"
```

---

## Task 15: Share/export (`IShareService`, `AndroidShareService`, `IDebugExportService`)

**Files:**
- Create: `src/MobileApp/IShareService.cs`
- Create: `src/MobileApp.Android/AndroidShareService.cs`
- Modify: `src/MobileApp.Android/MainActivity.cs` (DI registration)
- Create: `src/MobileApp/Debug/IDebugExportService.cs`

**Interfaces:**
- Consumes: `IDebugLogStore` (Task 2), `IDebugNetworkStore` (Task 3), `IDebugTelemetryStatus` (Task 4), `App.Instance.{AppVersion,DeviceName,DeviceType}` (Task 8).
- Produces: `MobileApp.IShareService.ShareText(string subject, string content)`; `MobileApp.Android.AndroidShareService`; `MobileApp.Debug.IDebugExportService.BuildDiagnosticsBundle() : string`. Wired into the Debug shell's share button in Task 16.

- [ ] **Step 1: Define `IShareService`**

Create `src/MobileApp/IShareService.cs`:

```csharp
namespace MobileApp;

public interface IShareService
{
    void ShareText(string subject, string content);
}
```

- [ ] **Step 2: Implement `AndroidShareService`**

Create `src/MobileApp.Android/AndroidShareService.cs`:

```csharp
using AndroidContent = Android.Content;

namespace MobileApp.Android;

public class AndroidShareService : IShareService
{
    public void ShareText(string subject, string content)
    {
        var intent = new AndroidContent.Intent(AndroidContent.Intent.ActionSend);
        intent.SetType("text/plain");
        intent.PutExtra(AndroidContent.Intent.ExtraSubject, subject);
        intent.PutExtra(AndroidContent.Intent.ExtraText, content);

        var chooser = AndroidContent.Intent.CreateChooser(intent, subject);
        chooser?.AddFlags(AndroidContent.ActivityFlags.NewTask);
        global::Android.App.Application.Context.StartActivity(chooser);
    }
}
```

- [ ] **Step 3: Register it on Android only**

In `src/MobileApp.Android/MainActivity.cs`, in `AndroidApp.RegisterPlatformServices`, replace:

```csharp
    protected override void RegisterPlatformServices(IServiceCollection services)
    {
        services
            .AddSingleton<IBrowserService, AndroidBrowserService>()
            .AddSingleton<IRedirectManager, AndroidRedirectManager>();
    }
```

with:

```csharp
    protected override void RegisterPlatformServices(IServiceCollection services)
    {
        services
            .AddSingleton<IBrowserService, AndroidBrowserService>()
            .AddSingleton<IRedirectManager, AndroidRedirectManager>()
            .AddSingleton<IShareService, AndroidShareService>();
    }
```

Desktop's `RegisterPlatformServices` (in `src/MobileApp.Desktop/Program.cs`) is deliberately left unchanged — no `IShareService` registration there; Task 16's share button resolves it as optional (`GetService<IShareService>()`, not `GetRequiredService`) and falls back to the existing file-picker pattern when it's `null`.

- [ ] **Step 4: Implement `IDebugExportService`**

Create `src/MobileApp/Debug/IDebugExportService.cs`:

```csharp
using System;
using System.Linq;
using System.Text;

namespace MobileApp.Debug;

public interface IDebugExportService
{
    string BuildDiagnosticsBundle();
}

public sealed class DebugExportService(
    IDebugLogStore logStore,
    IDebugNetworkStore networkStore,
    IDebugTelemetryStatus telemetryStatus) : IDebugExportService
{
    public string BuildDiagnosticsBundle()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"TrueMobile diagnostics bundle — {DateTimeOffset.UtcNow:u}");
        sb.AppendLine($"App version: {App.Instance.AppVersion}");
        sb.AppendLine($"Device: {App.Instance.DeviceName} ({App.Instance.DeviceType})");
        sb.AppendLine();

        var telemetry = telemetryStatus.Snapshot();
        sb.AppendLine("== Telemetry ==");
        sb.AppendLine($"Last success: {telemetry.LastSuccessAt?.ToString("u") ?? "none"}");
        sb.AppendLine($"Last failure: {telemetry.LastFailureAt?.ToString("u") ?? "none"} {telemetry.LastFailureMessage}");
        sb.AppendLine();

        sb.AppendLine("== Network (most recent first) ==");
        foreach (var entry in networkStore.Snapshot().Reverse())
        {
            sb.AppendLine($"[{entry.Timestamp:u}] {entry.Method} {entry.Uri} -> {entry.StatusCode?.ToString() ?? "ERROR"} ({entry.DurationMs}ms) {entry.Error}");
        }
        sb.AppendLine();

        sb.AppendLine("== Logs (most recent first) ==");
        foreach (var entry in logStore.Snapshot().Reverse())
        {
            sb.AppendLine($"[{entry.Timestamp:u}] [{entry.Level}] {entry.Category}: {entry.Message}");
        }

        return sb.ToString();
    }
}
```

Note: tokens/secrets are never read here — nothing in `logStore`/`networkStore`/`telemetryStatus`/device info carries raw token values, satisfying the "never export secrets" constraint by construction (there's nothing to redact because it was never included).

- [ ] **Step 5: Verify it builds**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

Run: `dotnet build src/MobileApp.Android/MobileApp.Android.csproj -p:AndroidSdkDirectory="<path-to-android-sdk>"` if an Android SDK is available.
Expected: Build succeeded.

- [ ] **Step 6: Commit**

```bash
git add src/MobileApp/IShareService.cs src/MobileApp.Android/AndroidShareService.cs src/MobileApp.Android/MainActivity.cs src/MobileApp/Debug/IDebugExportService.cs
git commit -m "Add share service and diagnostics bundle builder"
```

---

## Task 16: Debug tab shell, navigation, and final wiring

**Files:**
- Create: `src/MobileApp/ViewModels/DebugViewModel.cs`
- Create: `src/MobileApp/Views/DebugView.axaml`
- Create: `src/MobileApp/Views/DebugView.axaml.cs`
- Modify: `src/MobileApp/ViewModels/MainViewModel.cs`
- Modify: `src/MobileApp/Views/MainView.axaml`
- Modify: `src/MobileApp/App.axaml.cs`

**Interfaces:**
- Consumes: all 7 sub-screen ViewModels (Tasks 9–14), `IDebugExportService` (Task 15), `IShareService` (Task 15, optional/Android-only).
- Produces: the finished, navigable Debug tab.

- [ ] **Step 1: Implement `DebugViewModel`**

Create `src/MobileApp/ViewModels/DebugViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugViewModel : ViewModelBase
{
    private readonly DebugLogsViewModel _logsViewModel;
    private readonly DebugAuthLogsViewModel _authLogsViewModel;
    private readonly DebugNetworkViewModel _networkViewModel;
    private readonly DebugTokensViewModel _tokensViewModel;
    private readonly DebugDeviceInfoViewModel _deviceInfoViewModel;
    private readonly DebugTelemetryViewModel _telemetryViewModel;
    private readonly DebugStorageViewModel _storageViewModel;
    private readonly IDebugExportService _exportService;

    public DebugViewModel(
        DebugLogsViewModel logsViewModel,
        DebugAuthLogsViewModel authLogsViewModel,
        DebugNetworkViewModel networkViewModel,
        DebugTokensViewModel tokensViewModel,
        DebugDeviceInfoViewModel deviceInfoViewModel,
        DebugTelemetryViewModel telemetryViewModel,
        DebugStorageViewModel storageViewModel,
        IDebugExportService exportService)
    {
        _logsViewModel = logsViewModel;
        _authLogsViewModel = authLogsViewModel;
        _networkViewModel = networkViewModel;
        _tokensViewModel = tokensViewModel;
        _deviceInfoViewModel = deviceInfoViewModel;
        _telemetryViewModel = telemetryViewModel;
        _storageViewModel = storageViewModel;
        _exportService = exportService;
    }

    [ObservableProperty] private ViewModelBase? _currentSubViewModel;

    [RelayCommand] private void OpenLogs() => CurrentSubViewModel = _logsViewModel;
    [RelayCommand] private void OpenAuthLogs() => CurrentSubViewModel = _authLogsViewModel;
    [RelayCommand] private void OpenNetwork() => CurrentSubViewModel = _networkViewModel;
    [RelayCommand] private void OpenTokens() => CurrentSubViewModel = _tokensViewModel;
    [RelayCommand] private void OpenDeviceInfo() => CurrentSubViewModel = _deviceInfoViewModel;
    [RelayCommand] private void OpenTelemetry() => CurrentSubViewModel = _telemetryViewModel;
    [RelayCommand] private void OpenStorage() => CurrentSubViewModel = _storageViewModel;
    [RelayCommand] private void GoBack() => CurrentSubViewModel = null;

    public string BuildDiagnosticsBundle() => _exportService.BuildDiagnosticsBundle();
}
```

- [ ] **Step 2: Implement `DebugView`**

Create `src/MobileApp/Views/DebugView.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             x:Class="MobileApp.Views.DebugView"
             x:DataType="vm:DebugViewModel"
             Background="{DynamicResource Charcoal}">
  <Grid RowDefinitions="Auto,*" Margin="20 10 20 20">
    <Button Grid.Row="0" Content="&lt; Back" Command="{Binding GoBackCommand}"
            HorizontalAlignment="Left" Margin="0 0 0 10"
            Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"
            IsVisible="{Binding CurrentSubViewModel, Converter={x:Static ObjectConverters.IsNotNull}}"/>

    <StackPanel Grid.Row="1" Spacing="10" IsVisible="{Binding CurrentSubViewModel, Converter={x:Static ObjectConverters.IsNull}}">
      <TextBlock Text="Debug" FontSize="18" FontWeight="DemiBold" Foreground="{DynamicResource PureWhite}"/>
      <Button Content="Logs" Command="{Binding OpenLogsCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Auth / Deep-links" Command="{Binding OpenAuthLogsCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Network" Command="{Binding OpenNetworkCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Tokens" Command="{Binding OpenTokensCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Device Info" Command="{Binding OpenDeviceInfoCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Telemetry" Command="{Binding OpenTelemetryCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button Content="Storage" Command="{Binding OpenStorageCommand}" HorizontalAlignment="Stretch" Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}"/>
      <Button x:Name="ShareButton" Content="Share diagnostics" HorizontalAlignment="Stretch"
              Background="{DynamicResource Lavender}" Foreground="{DynamicResource Charcoal}" Margin="0 20 0 0"/>
    </StackPanel>

    <TransitioningContentControl Grid.Row="1" Content="{Binding CurrentSubViewModel}"
                                  IsVisible="{Binding CurrentSubViewModel, Converter={x:Static ObjectConverters.IsNotNull}}"/>
  </Grid>
</UserControl>
```

- [ ] **Step 3: Wire the share button in code-behind**

Create `src/MobileApp/Views/DebugView.axaml.cs`:

```csharp
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using MobileApp.ViewModels;

namespace MobileApp.Views;

public partial class DebugView : UserControl
{
    public DebugView()
    {
        InitializeComponent();
        ShareButton.Click += ShareButton_Click;
    }

    private DebugViewModel ViewModel => (DebugViewModel)DataContext!;

    private async void ShareButton_Click(object? sender, RoutedEventArgs e)
    {
        var bundle = ViewModel.BuildDiagnosticsBundle();
        var shareService = App.Instance.Services.GetService<IShareService>();

        if (shareService is not null)
        {
            shareService.ShareText("TrueMobile diagnostics", bundle);
            return;
        }

        await SaveToFileAsync(bundle);
    }

    private async Task SaveToFileAsync(string bundle)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is null) return;

        var file = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Diagnostics",
            SuggestedFileName = "truemobile-diagnostics.txt",
            FileTypeChoices = [new FilePickerFileType("Text") { Patterns = ["*.txt"] }]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(bundle);
    }
}
```

- [ ] **Step 4: Wire `DebugViewModel` into `MainViewModel`**

In `src/MobileApp/ViewModels/MainViewModel.cs`, add the property and constructor parameter (the existing `*ButtonFontWeight`/`*ButtonForeground` bookkeeping is unused by the current `MainView.axaml` — the built-in `TabbedPage` renders its own tab bar — so it's left untouched):

Replace:

```csharp
    [ObservableProperty] private PaymentViewModel _paymentViewModel;
    [ObservableProperty] private DataViewModel _dataViewModel;
    [ObservableProperty] private SettingsViewModel _settingsViewModel;

    public MainViewModel(PaymentViewModel paymentViewModel, DataViewModel dataViewModel, SettingsViewModel settingsViewModel)
    {
        _paymentViewModel = paymentViewModel;
        _dataViewModel = dataViewModel;
        _settingsViewModel = settingsViewModel;
```

with:

```csharp
    [ObservableProperty] private PaymentViewModel _paymentViewModel;
    [ObservableProperty] private DataViewModel _dataViewModel;
    [ObservableProperty] private SettingsViewModel _settingsViewModel;
    [ObservableProperty] private DebugViewModel _debugViewModel;

    public MainViewModel(PaymentViewModel paymentViewModel, DataViewModel dataViewModel, SettingsViewModel settingsViewModel, DebugViewModel debugViewModel)
    {
        _paymentViewModel = paymentViewModel;
        _dataViewModel = dataViewModel;
        _settingsViewModel = settingsViewModel;
        _debugViewModel = debugViewModel;
```

- [ ] **Step 5: Add the 4th tab in `MainView.axaml`**

In `src/MobileApp/Views/MainView.axaml`, add after the `Settings` `ContentPage` and before the closing `</TabbedPage>`:

```xml
  <ContentPage Header="Debug" Content="{Binding DebugViewModel}">
    <ContentPage.Icon>
      <PathIcon Data="{DynamicResource LineHorizontal3Regular}"/>
    </ContentPage.Icon>
  </ContentPage>
```

(`LineHorizontal3Regular` is already defined in `src/MobileApp/Assets/Icons.axaml` and unused elsewhere — no new icon resource needed.)

- [ ] **Step 6: Register everything in DI**

In `src/MobileApp/App.axaml.cs`, add to the `services` chain (same chain used in Tasks 2–4):

```csharp
            .AddSingleton<DebugLogsViewModel>()
            .AddSingleton<DebugAuthLogsViewModel>()
            .AddSingleton<DebugNetworkViewModel>()
            .AddSingleton<DebugTokensViewModel>()
            .AddSingleton<DebugDeviceInfoViewModel>()
            .AddSingleton<DebugTelemetryViewModel>()
            .AddSingleton<DebugStorageViewModel>()
            .AddSingleton<DebugViewModel>()
            .AddSingleton<IDebugExportService, DebugExportService>()
```

`MainViewModel` is already registered via `.AddSingleton<MainViewModel>()` — the container will resolve its new `DebugViewModel` constructor parameter automatically once `DebugViewModel` is registered too; registration order within the chain doesn't matter.

- [ ] **Step 7: Build both platforms**

Run: `dotnet build src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: Build succeeded.

Run: `dotnet build src/MobileApp.Android/MobileApp.Android.csproj -p:AndroidSdkDirectory="<path-to-android-sdk>"` if an Android SDK is available.
Expected: Build succeeded.

- [ ] **Step 8: Manual smoke test (Desktop)**

Run: `dotnet run --project src/MobileApp.Desktop/MobileApp.Desktop.csproj`
Expected: App launches with a 4th "Debug" tab. Tapping it shows the 7-item menu; each item navigates to its screen and "< Back" returns to the menu; "Share diagnostics" opens a save-file dialog and writes a non-empty `.txt` bundle.

- [ ] **Step 9: Run the full test suite one more time**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj`
Expected: PASS (all tests from Tasks 1 and 6).

- [ ] **Step 10: Commit**

```bash
git add src/MobileApp/ViewModels/DebugViewModel.cs src/MobileApp/Views/DebugView.axaml src/MobileApp/Views/DebugView.axaml.cs src/MobileApp/ViewModels/MainViewModel.cs src/MobileApp/Views/MainView.axaml src/MobileApp/App.axaml.cs
git commit -m "Wire the Debug tab into MainView and complete DI registration"
```
