# DataViewModel.cs — Code Review Findings

> All findings resolved.

| # | Finding | Fix |
|---|---------|-----|
| 1 | Nullable guard in `OnErrorsCollectionChanged` fired on Remove/Clear | `e.Action != NotifyCollectionChangedAction.Add` |
| 2 | `GetAccountsAsync` iterating live `Tokens` mid-await | `Tokens.ToList()` snapshot |
| 3 | Concurrent `RefreshTokenAsync` bypassing `IsRunning` guard | Both sites use `RefreshTokenCommand.ExecuteAsync(null)` |
| 4 | `SettingsRestoredMessage` handler mutating `ObservableCollection` off UI thread | Wrapped in `Dispatcher.UIThread.Post()` |
| 5 | `SettingsRestoredMessage` handler skipping `DataProviderAddedMessage` for restored providers | `foreach (var token in tokens) _messenger.Send(new DataProviderAddedMessage(...))` after `AddRange` |
| 6 | `Loading` stuck `true` when `RefreshTokenAsync` throws | `try/finally { Loading = false; }` |
| 7 | `Bitmap` allocated on every access, never disposed | `static readonly` field + `Dictionary<string, Bitmap>` with `GetValueOrDefault` |
| 8 | Load-refresh block duplicated between constructor and `SettingsRestoredMessage` handler | Extracted `LoadStoredTokens()`; fixed misleading `"Token is null"` log |
| 9 | Activity setup two-liner copy-pasted across methods | Extracted `StartActivity(string op)` static helper |
| 10 | `Loading = false` redundant inside `try` (dead code) | Removed; `finally` covers all paths |
