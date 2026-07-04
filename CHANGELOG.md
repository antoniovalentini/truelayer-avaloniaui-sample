# Changelog

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
