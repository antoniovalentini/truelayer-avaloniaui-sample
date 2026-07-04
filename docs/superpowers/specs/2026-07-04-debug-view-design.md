# Debug View — Design Spec

Date: 2026-07-04
Status: Approved (pending user sign-off on this document)
Source proposal: `docs/DEBUG_VIEW_PROPOSAL.md`
Related: `docs/SOLUTION_FEATURES.md`, `docs/LIVE_TESTING_CAPABILITIES.md`

## 1. Goal & Scope

Add an in-app "Debug" tab to the TrueLayer AvaloniaUI sample app (Desktop + Android) for live troubleshooting, covering all 9 features from `docs/DEBUG_VIEW_PROPOSAL.md`:

1. In-app log viewer
2. Network/API call inspector
3. OAuth/deep-link tracer
4. Token/account state inspector
5. Device & app info panel
6. OTel export status
7. Storage inspector
8. Crash/unhandled-exception log (folded into #1)
9. Share/export diagnostics button

Guardrails (carried over from the proposal and confirmed during brainstorming):
- No general-purpose structured logging framework, no pluggable sinks, no configurable retention, no remote telemetry shipping. Bounded in-memory ring buffers only.
- No new third-party UI/navigation dependency — reuse the app's existing `ViewLocator` + `TransitioningContentControl` idiom.
- Available in both Debug and Release builds (it's a demo-app feature, not a dev-only tool), except where noted.

## 2. Navigation & Structure

- New 4th tab "Debug" added to `MainView`'s `TabbedPage`, bound to a new `DebugViewModel` registered in DI alongside the existing three tab view models.
- `DebugViewModel` exposes:
  - A static menu of 7 entries: Logs, Network, Auth/Deep-links, Tokens, Device Info, Telemetry, Storage.
  - A nullable `CurrentSubViewModel` property.
- Selecting a menu entry sets `CurrentSubViewModel` to the corresponding sub-view-model instance. A `TransitioningContentControl` bound to `CurrentSubViewModel`, combined with the existing `ViewLocator`, resolves and renders the matching sub-view — the same mechanism `MainView` already uses at the top level, applied one level deeper.
- A back command clears `CurrentSubViewModel` back to `null`, returning to the menu.
- Each sub-screen is an independent `UserControl` + `ViewModel` pair (e.g. `DebugLogsView`/`DebugLogsViewModel`), matching the existing one-view-per-feature pattern (`DataView`, `PaymentView`, `SettingsView`).
- A "Share diagnostics" action lives on the Debug menu screen itself (not duplicated per sub-screen).

## 3. Logs & Crash Capture

- New singleton `IDebugLogStore`: a fixed-capacity ring buffer (500 entries) of `{Timestamp, Level, Category, Message, Exception?}`. Thread-safe; oldest-first eviction when full. No configuration surface (matches the "no retention config" guardrail).
- New `InMemoryLoggerProvider` (`ILoggerProvider`/`ILogger`) registered via `builder.AddProvider(...)` next to the existing `AddConsole()` call in `App.axaml.cs`. Every `ILogger<T>` call already made throughout the app flows into `IDebugLogStore` automatically — no call-site changes required anywhere else in the codebase.
- Crash capture reuses the same store: `AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException` are hooked once at startup (in `App.axaml.cs`) and each writes a single `Critical`-level entry into `IDebugLogStore`. This does not suppress or prevent the crash — it only records it before the process continues its normal (fatal, for `UnhandledException`) behavior.
- `DebugLogsViewModel`: displays the buffer newest-first, with a level filter and in-memory text search (no query language, no persistence).

Failure handling: `InMemoryLoggerProvider` must never throw or block the log call it's observing. Any internal failure (e.g. a formatting exception) is caught and the entry is dropped, mirroring the existing defensive try/catch pattern already used in `AuthTokenStorage`.

## 4. Network Inspector

- New singleton `IDebugNetworkStore`: fixed-capacity ring buffer (100 entries) of `{Timestamp, Method, Uri, StatusCode, DurationMs, TraceId?, ErrorSnippet?}`.
- New `DebugHttpLoggingHandler : DelegatingHandler`, registered via:
  ```csharp
  services.AddHttpClient<IApiClient, ApiClient>()
      .AddHttpMessageHandler(sp => new DebugHttpLoggingHandler(sp.GetRequiredService<IDebugNetworkStore>()));
  ```
  placed **after** the existing `.AddTrueLayer(...)` call in `App.axaml.cs`. Because the TrueLayer SDK already registers its client via `services.AddHttpClient<IApiClient, ApiClient>()` (confirmed in `libs/truelayer-dotnet-data/src/TrueLayer/TrueLayerServiceCollectionExtensions.cs`), this layers an extra handler onto the same named client's pipeline without modifying the vendored submodule.
- The handler records method/URI before sending, and status code/duration/a truncated response-body snippet after receiving (truncation length fixed in code, not configurable — bounds memory use per guardrail).
- `DebugNetworkViewModel`: newest-first list; tapping an entry shows full captured detail.
- Handler failures (e.g. cannot read response body) are caught and logged with a partial entry — the underlying HTTP call must always proceed and its result must always be returned unmodified to the real caller.

## 5. Telemetry (OTel) Status

- Remove the `#if DEBUG` guard around `_ = new OtelDiagnosticsListener();` in `App.axaml.cs` — it now always runs, in both Debug and Release builds.
- Extend `OtelDiagnosticsListener` minimally: alongside its existing console `Console.WriteLine`, it also updates a new singleton `IDebugTelemetryStatus` with the timestamp + message of the most recent event whose name/level indicates a successful export vs. a failure, based on the same `EventWrittenEventArgs` it already receives. No deeper coupling to OTel exporter internals than pattern-matching on event name/level already used for the console line.
- `DebugTelemetryViewModel` displays: last successful export time (if any), last failure time + message (if any), and the configured Honeycomb endpoint (`config["Honeycomb:Endpoint"]`) — **never** the API key.

## 6. Tokens Screen

- `OAuthToken` record gains an `IssuedAt` (`DateTimeOffset`, UTC) field.
- `DataViewModel.AddTokenAsync` sets `IssuedAt` to the current time whenever a token is added or refreshed.
- Backward compatibility: existing exported/stored JSON without `IssuedAt` deserializes it to `default(DateTimeOffset)` (i.e. year 1), which the Tokens screen will display as "expired" — harmless, self-corrects on the next refresh, and does not affect `LoadTokens`/`StoreTokens`/API calls (which don't consume `IssuedAt`).
- `DebugTokensViewModel` reuses `IAuthTokenStorage.LoadTokens()` directly (no new storage abstraction) and computes `IssuedAt + TimeSpan.FromSeconds(ExpiresIn)` vs. now to show "expires in Xm" / "expired Xm ago" per provider.
- Access/refresh token values are masked by default (e.g. `••••1a2b`, last 4 characters visible) with a tap-to-reveal toggle per row.

## 7. Device & App Info Screen

- Static display of values already computed on `App`: `AppVersion`, `DeviceId`, `DeviceName`, `DeviceType`, plus `Environment.Version` (.NET runtime version). No new data sources; purely a read-only display screen.

## 8. Storage Inspector

- Small addition to `IAuthTokenStorage`: a new method (e.g. `IReadOnlyList<StorageFileSnapshot> InspectManagedFiles()`) returning `{FileName, FullPath, SizeBytes, LastModifiedUtc, Exists}` for the files it manages (`settings.json` under the secrets folder, `beneficiaries.json` under the base path). This keeps path computation (`BasePath`/`SecretsFolderPath`) inside the existing storage abstraction rather than duplicating it in the Debug feature.
- `DebugStorageViewModel` lists each file's metadata, with an optional "view content" toggle per file that pretty-prints the JSON. For `settings.json`, any token-shaped string values are passed through the same masking helper used by the Tokens screen (one masking utility, two call sites) before display.

## 9. Share / Export Diagnostics

- New `IDebugExportService.BuildDiagnosticsBundle()` assembling a single plain-text bundle: recent log entries, recent network entries, device info, and telemetry status. **Tokens and secrets are excluded from the bundle entirely** — troubleshooting doesn't require raw secrets, and excluding them avoids any masked-vs-unmasked inconsistency in a file that leaves the device.
- Desktop: reuses the existing `SaveFilePickerAsync` pattern already implemented in `SettingsView.axaml.cs` for settings export.
- Android: new `IShareService.ShareText(string)`, implemented via `Intent.ActionSend` + `EXTRA_TEXT`, following the same per-platform split already used for `IBrowserService` (`AndroidBrowserService`/`DesktopBrowserService`). No `AndroidManifest`/`FileProvider` changes.
  - Known ceiling: Android's binder transaction limit (~1MB) bounds how much text `EXTRA_TEXT` can carry. The fixed-size ring buffers (500 logs, 100 network calls) keep the bundle well under this in practice. If this ever becomes insufficient, the upgrade path is a `FileProvider`-based file share — not needed for v1.
- Exposed as a single "Share diagnostics" button on the Debug menu screen.

## 10. Error Handling Summary

- All new stores are fixed-capacity, evict oldest-first, and never throw on write.
- Logging/network instrumentation never alters or blocks the operation it observes; internal failures are swallowed and logged (not surfaced to the user).
- `AppDomain.UnhandledException` handling only records — it cannot and must not suppress the crash.
- File reads in the storage inspector follow the existing defensive try/catch + log pattern from `AuthTokenStorage`; a missing/corrupt file renders as "not found"/"unreadable" rather than crashing the screen.
- Masking happens at the display layer only. Stores retain raw values in memory (required for reveal-on-tap and existing refresh logic); the export bundle never includes tokens regardless of masking state.

## 11. Testing

No test project exists in the solution today. Scope is deliberately narrow — a new xunit project (`test/MobileApp.Tests`) covering only the non-trivial pure logic introduced here:
- `IDebugLogStore` / `IDebugNetworkStore`: fixed-capacity behavior and oldest-first eviction.
- Token expiry calculation (`IssuedAt + ExpiresIn` vs. now), including the boundary/backward-compatibility case (`default(DateTimeOffset)` renders as expired).
- Token masking helper output format.

Not covered by automated tests (by design, per YAGNI): navigation wiring, the HTTP delegating handler, and platform share intents — these are thin plumbing best verified by running the app.

## 12. Out of Scope (for this spec)

- Any dev-only gating (build flags, hidden gestures) — the Debug tab is always visible, in both build configurations.
- Persisting logs/network calls across app restarts.
- Remote log shipping or a pluggable sink model.
- Modifying the vendored `libs/truelayer-dotnet-data` submodule.
