# Data tab navigation stack — foundations

## Goal

Add stack-based navigation inside the "Data" tab of `MainView` (a `TabbedPage`), so tapping a balance card pushes a Transactions page for that account, with a back button to return to the balances list. This is a foundations-only pass: the Transactions page is a placeholder (no real transactions API call — the TrueLayer SDK fork only exposes `GetAccounts`/`GetAccountBalance`, not `GetTransactions`).

## Reference

Avalonia 12.0.5 ships `Page`/`ContentPage`/`NavigationPage`/`TabbedPage` (stack-based navigation via `NavigationPage`, docs at repo-root `NavigationPage.md`). Confirmed against the Avalonia team's own sample project (`/home/lol/projects/z_Trash/TestMobileNavigationAvalonia12`): pushed pages are `ContentPage` subclasses constructed directly in code-behind, and navigation is triggered via `this.Navigation.PushAsync(...)` from a plain `Click` handler — no navigation-service abstraction.

## Current state

- `MainView.axaml` is a `TabbedPage` with 4 tabs, each a `ContentPage` whose `Content` is bound to a ViewModel (e.g. `Content="{Binding DataViewModel}"`), resolved to the matching `UserControl` View by `ViewLocator` (name convention: `XViewModel` → `XView`).
- `DataView.axaml` is a `UserControl` (not a `Page`) showing an `ItemsControl` of `ProviderBalance` cards.
- `ProviderBalance` is a record: `Id` (accountId), `Iban`, `AvailableAmount`, `CurrentAmount`, `Overdraft`, `IconSource`. No `AccessToken`/`ProviderId`.
- No navigation of any kind exists yet anywhere in the app (`grep` for `PushAsync`/`NavigationPage` returns nothing outside this design).

## Design

### Architecture

- `MainView.axaml`: the "Data" tab changes from
  ```xml
  <ContentPage Header="Data" Content="{Binding DataViewModel}">
    <ContentPage.Icon><PathIcon Data="{DynamicResource BuildingBankRegular}"/></ContentPage.Icon>
  </ContentPage>
  ```
  to
  ```xml
  <NavigationPage Header="Data">
    <NavigationPage.Icon><PathIcon Data="{DynamicResource BuildingBankRegular}"/></NavigationPage.Icon>
    <ContentPage Header="Balances" NavigationPage.HasNavigationBar="False" Content="{Binding DataViewModel}"/>
  </NavigationPage>
  ```
  `HasNavigationBar="False"` on the root avoids a redundant second header bar sitting above `DataView`'s own inline "Accounts" header row. `Icon`/`Header` on `NavigationPage` (inherited from `Page`) keep driving the tab-strip label/icon exactly as `ContentPage` did before — `MainViewModel.OnSelectionChanged`'s `Header`-string comparison is unaffected.
- `DataView.axaml`: the `Border` inside `ItemsControl.ItemTemplate` (the balance card) is wrapped in a `Button` (transparent background, no border, zero padding — visually identical to today) with a `Click` handler.
- `DataView.axaml.cs`: new handler reads the `ProviderBalance` from the clicked button's `DataContext`, resolves `Navigation` via `this.FindAncestorOfType<Page>()?.Navigation` (borrows the containing "Balances" `ContentPage`'s `Navigation`, since `DataView` itself is a `UserControl`, not a `Page`), and calls `Navigation.PushAsync(new TransactionsView { DataContext = new TransactionsViewModel(balance) })`.
- New `TransactionsViewModel` (`MobileApp.ViewModels`) and `TransactionsView : ContentPage` (`MobileApp.Views`), following the sample's pattern directly — constructed imperatively in code-behind, no DI, no ViewLocator involvement.
- Back navigation is free: `NavigationPage`'s built-in back button calls `PopAsync()` automatically once `CanGoBack` is true (stack depth > 1).

### Components

- `TransactionsViewModel`: takes a `ProviderBalance` in its constructor, exposes `Iban`, `AvailableAmount`, `CurrentAmount`, `Overdraft` for display. No async work, no services.
- `TransactionsView`/`.axaml.cs`: `ContentPage` subclass, `Header="Transactions"`, a `StackPanel` of `TextBlock`s bound to the ViewModel's fields. Default `HasNavigationBar`/`HasBackButton` (both `true`) so the back button shows automatically.

### Data flow

1. User taps a balance card → `Button.Click` fires in `DataView.axaml.cs`.
2. Handler reads the `ProviderBalance` off the button's `DataContext`.
3. Handler resolves `Navigation` via `this.FindAncestorOfType<Page>()`.
4. `await Navigation.PushAsync(new TransactionsView { DataContext = new TransactionsViewModel(balance) })` — animates in, updates `NavigationStack`/`CanGoBack`, shows the nav bar with "Transactions" + auto back button.
5. `TransactionsView` renders the placeholder text from the `ProviderBalance` already in memory — no network call.
6. Back tap → `NavigationPage.PopAsync()` returns to the same "Balances" `ContentPage` instance (state untouched, since it was never destroyed). Repeat from step 1 for another account.

### Error handling / edge cases

- `Navigation` could be `null` if resolved before the visual tree attaches — handler no-ops (`?.`), matching the sample's own `if (this.Navigation != null)` guard.
- No loading/error states in `TransactionsView` — no async work yet.
- Rapid double-taps could push two `TransactionsView`s back-to-back; out of scope for this pass (`NavigationPage` doesn't debounce). Revisit if it's actually a problem in practice.

### Testing

- No unit tests — this is UI wiring (event handler + XAML) with no existing UI/interaction test harness in the project to extend.
- Manual verification: run the app, refresh tokens so `Balances` populates, tap a card, confirm the Transactions placeholder shows the right IBAN/amount, tap back, confirm return to the same balances list, repeat for a different account.

## Explicitly out of scope

- Real transaction fetching (`GetTransactions` doesn't exist in the TrueLayer SDK fork yet).
- Any change to `ProviderBalance`, `DataViewModel`, DI registration, or the TrueLayer SDK.
- Double-tap debouncing, loading/error states on the Transactions page.
