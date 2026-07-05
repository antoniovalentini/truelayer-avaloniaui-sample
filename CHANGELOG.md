# Changelog

## [0.1.3] - 2026-07-05

### Added

- Reusable `accent-hover` style (`Assets/Styles.axaml`) for button/checkbox hover and press feedback — can be applied to an individual control or to an ancestor to cascade to every descendant; used across the Settings, Data, and Debug views
- Dedicated bug icon for the Debug tab instead of the generic tab icon

### Changed

- Disabled buttons now keep legible (light) text against the theme's dark disabled background instead of low-contrast gray-on-gray
- Checkbox borders are now visible against dark panels, both at rest and on hover, instead of blending into the background
- Reduced the fake token-refresh delay used by design-time/dev fakes from 2s to 500ms for snappier iteration

### Fixed

- `IAuthTokenStorage` now creates an empty beneficiaries JSON file if it's missing instead of failing
- Token refresh error handling now falls back to `Problem.Detail` when `Problem.Errors` is empty, instead of calling `ExtractErrors` on a null dictionary
- `ApiClient` (TrueLayer SDK fork, submodule) now correctly parses OAuth-style error responses (`error`/`error_description`/`error_details`, e.g. rate limiting) into `ProblemDetails`, instead of constructing an invalid empty record
- README corrected to match the current codebase: .NET 10 SDK requirement, correct repository clone URL, Android target framework/API level, JDK provisioning note, and previously undocumented Settings/Debug tabs

## [0.1.2] - 2026-07-05

### Added

- New in-app "Debug" tab (Desktop + Android): log viewer with search/level/auth-only filters and crash capture, network call inspector, token inspector with masked/reveal-on-tap values, device info panel, and OpenTelemetry export status — all backed by fixed-capacity in-memory ring buffers, no persistence or remote shipping
- Storage inspector listing the app's managed files with size/last-modified metadata and a masked JSON content preview
- "Share diagnostics" action bundling logs/network/device/telemetry state into a single text export (tokens/secrets always excluded), via native share on Android and a file save on Desktop

## [0.1.1] - 2026-07-04

### Changed

- `GetAccountsAsync` now fetches accounts for all stored tokens, and balances for all accounts of a token, concurrently via `Task.WhenAll` instead of sequentially awaiting each one — cuts load time roughly proportional to account count
- Replaced the indeterminate `ProgressBar` on the Data page with a skeleton screen (shimmering placeholder cards) while accounts/balances are loading

## [0.1.0] - 2026-07-04

### Fixed

- `OnErrorsCollectionChanged` guard was firing `ClearErrorAsync` spuriously on Remove and Clear events due to a nullable `>=` comparison — replaced with `e.Action != NotifyCollectionChangedAction.Add`
- `GetAccountsAsync` was iterating the live `Tokens` list without a snapshot, causing `InvalidOperationException` when a message handler mutated the list mid-await — fixed with `.ToList()`
- Both `RefreshTokenAsync` fire-and-forget call sites were bypassing the `[RelayCommand]` concurrency guard — replaced with `RefreshTokenCommand.ExecuteAsync(null)`
- `SettingsRestoredMessage` handler was running `ObservableCollection` mutations on the sender's thread — wrapped in `Dispatcher.UIThread.Post()`
- `SettingsRestoredMessage` handler was not sending `DataProviderAddedMessage` for restored providers, leaving downstream subscribers without provider UI entries
- `Loading` flag was never reset when `RefreshTokenAsync` threw — added `try/finally`
- `Bitmap` objects for bank logos were allocated on every access and never disposed — replaced with `static readonly` field and `Dictionary<string, Bitmap>`

### Refactored

- Extracted `LoadStoredTokens()` to remove duplicated load-and-guard block between constructor and `SettingsRestoredMessage` handler; fixed misleading `"Token is null"` log message
- Extracted `StartActivity(string op)` static helper to remove duplicated activity setup across `ExchangeCode` and `RefreshTokenAsync`
- Removed redundant `Loading = false` inside `try` block — `finally` covers all paths
