# Debug View

The app has a fourth tab, "Debug", for live troubleshooting on a device without attaching a debugger. It's available in both Debug and Release builds — this is a demo-app feature, not a dev-only tool.

Everything it captures lives in fixed-capacity, in-memory ring buffers: nothing persists across an app restart, there's no remote log shipping, and no configuration surface for retention. This keeps the feature small and bounds its memory footprint.

## Screens

The Debug tab is a menu of five screens plus a share action, reusing the app's existing `ViewLocator` + `TransitioningContentControl` navigation pattern one level deeper than the main tab bar.

### Logs

Shows the last 500 log entries written through the standard `ILogger` pipeline (`InMemoryLoggerProvider` taps every `ILogger<T>` call already made throughout the app — no call-site changes needed elsewhere). Unhandled exceptions and unobserved task exceptions are also captured here as `Critical`-level entries; capturing them doesn't suppress the crash.

- Free-text search over the message
- Level filter (Trace–Critical)
- "Auth / deep-links only" toggle — narrows the list to the categories involved in the OAuth redirect flow (`AuthManager`, and the platform-specific redirect managers/activity), useful when chasing a login issue specifically

### Network

Shows the last 100 HTTP calls made through TrueLayer's `IApiClient`, captured by a `DelegatingHandler` layered onto the SDK's own `HttpClient` registration. Each entry has method, URI, status code, duration, trace ID (from the `Tl-Trace-Id` response header, when present), and — for non-success responses — a capped snippet of the response body. The instrumentation never alters or blocks the request/response it observes.

### Tokens

Lists the stored OAuth tokens per provider, with access/refresh token values masked by default (last 4 characters visible) and a per-row tap-to-reveal toggle. Expiry is computed from `IssuedAt + ExpiresIn` and shown as "expires in Xm" / "expired Xm ago".

### Device Info

A static, read-only panel: app version, device ID/name/type, and the .NET runtime version — plus telemetry status underneath: the configured OTel/Honeycomb endpoint and the timestamp (and message, on failure) of the most recent successful/failed trace export.

### Storage

Lists the files the app manages on disk (`settings.json`, `beneficiaries.json`) with size and last-modified time, and an optional pretty-printed content preview per file. Token-shaped values in the preview go through the same masking helper used by the Tokens screen.

### Share diagnostics

Bundles recent logs, recent network calls, device info, and telemetry status into a single plain-text blob — Desktop saves it to a file via the existing file-picker pattern, Android shares it via `Intent.ActionSend`. **Tokens and secrets are never included in the bundle**, regardless of their masked/revealed state on screen.

## Design notes

- No general-purpose logging framework, pluggable sinks, or remote telemetry — see the ring-buffer approach above.
- No new navigation/UI dependency; the Debug tab is built entirely from the app's existing `ViewLocator`/`TransitioningContentControl` idiom and one-view-per-screen convention.
- Views and view models live under `Views/Debug/` and `ViewModels/Debug/`, but keep the flat `MobileApp.Views`/`MobileApp.ViewModels` namespace on purpose — see the comment in `ViewLocator.cs`.
- Full design rationale: [`docs/superpowers/specs/2026-07-04-debug-view-design.md`](superpowers/specs/2026-07-04-debug-view-design.md).
