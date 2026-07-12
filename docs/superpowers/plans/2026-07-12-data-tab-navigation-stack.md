# Data Tab Navigation Stack Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a stack-based navigation flow to the "Data" tab so tapping a balance card pushes a placeholder Transactions page for that account, with a back button returning to the balances list.

**Architecture:** The "Data" tab's `ContentPage` becomes a `NavigationPage` whose root content is the existing balances `ContentPage` (unchanged `DataView`/`DataViewModel` binding). Each balance card becomes a `Button` whose `Click` handler resolves the ancestor `Page.Navigation` and pushes a new `TransactionsView : ContentPage` constructed directly with a `TransactionsViewModel`. No DI, no ViewLocator, no navigation-service abstraction — matches the Avalonia team's own sample pattern.

**Tech Stack:** Avalonia 12.0.5 (`Page`/`ContentPage`/`NavigationPage`/`TabbedPage`), CommunityToolkit.Mvvm, xUnit (existing `MobileApp.Tests` project).

## Global Constraints

- Avalonia version is pinned at 12.0.5 (`src/Directory.Packages.props`) — do not bump it.
- `AvaloniaUseCompiledBindingsByDefault` is `true` (`src/MobileApp/MobileApp.csproj`) — every `.axaml` file with a `{Binding ...}` must declare `x:DataType`.
- No changes to `ProviderBalance`, `DataViewModel`, DI registration in `App.axaml.cs`, or the TrueLayer SDK (`libs/truelayer-dotnet-data`) — out of scope per spec.
- No real transactions API call — the SDK fork only exposes `GetAccounts`/`GetAccountBalance` (`libs/truelayer-dotnet-data/src/TrueLayer/Data/IDataApi.cs`).
- `TransactionsView`/`TransactionsViewModel` are constructed directly in code (`new TransactionsView { DataContext = new TransactionsViewModel(...) }`) — they do NOT go through `ViewLocator`'s `XViewModel` → `XView` naming convention, so naming them differently would not break anything, but keep the `Transactions` prefix for consistency with the rest of the codebase.

Spec: `docs/superpowers/specs/2026-07-12-data-tab-navigation-stack-design.md`

---

### Task 1: TransactionsViewModel

**Files:**
- Create: `src/MobileApp/ViewModels/TransactionsViewModel.cs`
- Test: `src/MobileApp.Tests/TransactionsViewModelTests.cs`

**Interfaces:**
- Consumes: nothing (plain constructor args, no dependency on `ProviderBalance` or any Avalonia UI type — keeps the class testable without touching `Avalonia.Media.Imaging.Bitmap`).
- Produces: `TransactionsViewModel(string iban, string availableAmount, string currentAmount, string overdraft)` with public `get`-only properties `Iban`, `AvailableAmount`, `CurrentAmount`, `Overdraft` (all `string`). Task 2 and Task 4 bind/construct against these exact names.

- [ ] **Step 1: Write the failing test**

Create `src/MobileApp.Tests/TransactionsViewModelTests.cs`:

```csharp
using MobileApp.ViewModels;
using Xunit;

namespace MobileApp.Tests;

public class TransactionsViewModelTests
{
    [Fact]
    public void Constructor_ExposesGivenAccountFields()
    {
        var viewModel = new TransactionsViewModel("GB00TEST00000000000000", "£100.00", "£120.00", "£0.00");

        Assert.Equal("GB00TEST00000000000000", viewModel.Iban);
        Assert.Equal("£100.00", viewModel.AvailableAmount);
        Assert.Equal("£120.00", viewModel.CurrentAmount);
        Assert.Equal("£0.00", viewModel.Overdraft);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj --filter TransactionsViewModelTests`
Expected: FAIL to build — `TransactionsViewModel` does not exist yet (CS0246).

- [ ] **Step 3: Write minimal implementation**

Create `src/MobileApp/ViewModels/TransactionsViewModel.cs`:

```csharp
namespace MobileApp.ViewModels;

public class TransactionsViewModel(string iban, string availableAmount, string currentAmount, string overdraft)
{
    public string Iban { get; } = iban;
    public string AvailableAmount { get; } = availableAmount;
    public string CurrentAmount { get; } = currentAmount;
    public string Overdraft { get; } = overdraft;
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test src/MobileApp.Tests/MobileApp.Tests.csproj --filter TransactionsViewModelTests`
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add src/MobileApp/ViewModels/TransactionsViewModel.cs src/MobileApp.Tests/TransactionsViewModelTests.cs
git commit -m "Add TransactionsViewModel for the placeholder transactions page"
```

---

### Task 2: TransactionsView (placeholder page)

**Files:**
- Create: `src/MobileApp/Views/TransactionsView.axaml`
- Create: `src/MobileApp/Views/TransactionsView.axaml.cs`

**Interfaces:**
- Consumes: `MobileApp.ViewModels.TransactionsViewModel` from Task 1 (`Iban`, `AvailableAmount`, `CurrentAmount`, `Overdraft` properties).
- Produces: `MobileApp.Views.TransactionsView`, a `ContentPage` with a public parameterless constructor (`DataContext` assigned by the caller in Task 4, same as the reference sample's `new SettingsView { DataContext = new SettingsViewModel() }`).

There is no meaningful unit test for a XAML view with no logic in its code-behind — the test cycle for this task is "it compiles," which is the same signal the existing `DataView.axaml`/`.axaml.cs` pair relies on (no dedicated view tests anywhere in this codebase).

- [ ] **Step 1: Create the view XAML**

Create `src/MobileApp/Views/TransactionsView.axaml`:

```xml
<ContentPage xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:d="http://schemas.microsoft.com/expression/blend/2008"
             xmlns:mc="http://schemas.openxmlformats.org/markup-compatibility/2006"
             xmlns:vm="clr-namespace:MobileApp.ViewModels"
             mc:Ignorable="d" d:DesignWidth="540" d:DesignHeight="960"
             Header="Transactions"
             x:Class="MobileApp.Views.TransactionsView"
             x:DataType="vm:TransactionsViewModel"
             Background="{DynamicResource Charcoal}">
  <StackPanel Margin="20" Spacing="10">
    <TextBlock Text="{Binding Iban}" FontSize="18" FontWeight="DemiBold" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="{Binding AvailableAmount}" FontWeight="Bold" FontSize="22" Foreground="{DynamicResource PureWhite}"/>
    <TextBlock Text="Transaction history coming soon." Foreground="{DynamicResource PureWhite}" Margin="0 20 0 0"/>
  </StackPanel>
</ContentPage>
```

Note: no `Design.DataContext` block here (unlike `DataView.axaml`'s `DesignDataViewModel`) — `TransactionsViewModel` has no parameterless constructor, and adding one purely for the XAML previewer isn't worth it for a placeholder page.

- [ ] **Step 2: Create the code-behind**

Create `src/MobileApp/Views/TransactionsView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class TransactionsView : ContentPage
{
    public TransactionsView()
    {
        InitializeComponent();
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/MobileApp/MobileApp.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/Views/TransactionsView.axaml src/MobileApp/Views/TransactionsView.axaml.cs
git commit -m "Add TransactionsView placeholder page"
```

---

### Task 3: Wrap the Data tab in a NavigationPage

**Files:**
- Modify: `src/MobileApp/Views/MainView.axaml:15-19`

**Interfaces:**
- Consumes: nothing new — `{Binding DataViewModel}` keeps resolving against `MainViewModel.DataViewModel` exactly as before.
- Produces: a `NavigationPage` (`Header="Data"`) hosting the existing balances `ContentPage` as its root, giving Task 4's `Navigation.PushAsync` call a `NavigationPage` ancestor to push onto.

**Current content of `src/MobileApp/Views/MainView.axaml:15-19`:**

```xml
  <ContentPage Header="Data" Content="{Binding DataViewModel}">
    <ContentPage.Icon>
      <PathIcon Data="{DynamicResource BuildingBankRegular}"/>
    </ContentPage.Icon>
  </ContentPage>
```

- [ ] **Step 1: Replace the ContentPage with a NavigationPage**

Replace the block above with:

```xml
  <NavigationPage Header="Data">
    <NavigationPage.Icon>
      <PathIcon Data="{DynamicResource BuildingBankRegular}"/>
    </NavigationPage.Icon>
    <ContentPage Header="Balances" NavigationPage.HasNavigationBar="False" Content="{Binding DataViewModel}"/>
  </NavigationPage>
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/MobileApp/MobileApp.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 3: Manual check — tab strip unaffected**

Run the desktop app (see Task 5 for the exact run command) and confirm the "Data" tab still shows the bank icon and "Data" label in the tab strip, and the balances list still renders with no visible top navigation bar above it (that's `HasNavigationBar="False"` on the root page working as intended).

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/Views/MainView.axaml
git commit -m "Wrap the Data tab in a NavigationPage"
```

---

### Task 4: Wire balance card taps to push TransactionsView

**Files:**
- Modify: `src/MobileApp/Views/DataView.axaml:111-134`
- Modify: `src/MobileApp/Views/DataView.axaml.cs`

**Interfaces:**
- Consumes: `MobileApp.ViewModels.TransactionsViewModel(string iban, string availableAmount, string currentAmount, string overdraft)` from Task 1, `MobileApp.Views.TransactionsView` (parameterless constructor) from Task 2.
- Produces: nothing consumed by later tasks — this is the last code change.

**Current content of `src/MobileApp/Views/DataView.axaml:111-134`:**

```xml
      <!-- Balances -->
      <ItemsControl IsVisible="{Binding !Loading}" ItemsSource="{Binding Balances}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Border Background="{DynamicResource PureWhite}" CornerRadius="10" Margin="0 0 0 15" Padding="15">
              <StackPanel Spacing="12">
                <Grid ColumnDefinitions="Auto, *" RowDefinitions="Auto, Auto">
                  <!-- Logo -->
                  <Border Grid.RowSpan="2" Width="64" Height="64" BorderBrush="LightGray" BorderThickness="1" CornerRadius="8" ClipToBounds="True">
                    <Border.Background>
                      <ImageBrush Source="{Binding IconSource}" Stretch="UniformToFill"/>
                    </Border.Background>
                  </Border>

                  <!-- Account Info Section -->
                  <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding AvailableAmount}" HorizontalAlignment="Right"
                             FontWeight="Bold" FontSize="22" Foreground="{DynamicResource Charcoal}"/>
                  <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Iban}"
                             HorizontalAlignment="Right" VerticalAlignment="Bottom"/>
                </Grid>
              </StackPanel>
            </Border>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
```

- [ ] **Step 1: Wrap the card Border in a clickable Button**

Replace the block above with:

```xml
      <!-- Balances -->
      <ItemsControl IsVisible="{Binding !Loading}" ItemsSource="{Binding Balances}">
        <ItemsControl.ItemTemplate>
          <DataTemplate>
            <Button Click="OnBalanceCardClick" Background="Transparent" BorderThickness="0" CornerRadius="10" Padding="0" HorizontalContentAlignment="Stretch">
              <Border Background="{DynamicResource PureWhite}" CornerRadius="10" Margin="0 0 0 15" Padding="15">
                <StackPanel Spacing="12">
                  <Grid ColumnDefinitions="Auto, *" RowDefinitions="Auto, Auto">
                    <!-- Logo -->
                    <Border Grid.RowSpan="2" Width="64" Height="64" BorderBrush="LightGray" BorderThickness="1" CornerRadius="8" ClipToBounds="True">
                      <Border.Background>
                        <ImageBrush Source="{Binding IconSource}" Stretch="UniformToFill"/>
                      </Border.Background>
                    </Border>

                    <!-- Account Info Section -->
                    <TextBlock Grid.Row="0" Grid.Column="1" Text="{Binding AvailableAmount}" HorizontalAlignment="Right"
                               FontWeight="Bold" FontSize="22" Foreground="{DynamicResource Charcoal}"/>
                    <TextBlock Grid.Row="1" Grid.Column="1" Text="{Binding Iban}"
                               HorizontalAlignment="Right" VerticalAlignment="Bottom"/>
                  </Grid>
                </StackPanel>
              </Border>
            </Button>
          </DataTemplate>
        </ItemsControl.ItemTemplate>
      </ItemsControl>
```

- [ ] **Step 2: Add the click handler in code-behind**

Current `src/MobileApp/Views/DataView.axaml.cs`:

```csharp
using Avalonia.Controls;

namespace MobileApp.Views;

public partial class DataView : UserControl
{
    public DataView()
    {
        InitializeComponent();
    }
}
```

Replace with:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MobileApp.Models;
using MobileApp.ViewModels;

namespace MobileApp.Views;

public partial class DataView : UserControl
{
    public DataView()
    {
        InitializeComponent();
    }

    private void OnBalanceCardClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ProviderBalance balance }) return;

        var navigation = this.FindAncestorOfType<Page>()?.Navigation;
        navigation?.PushAsync(new TransactionsView
        {
            DataContext = new TransactionsViewModel(balance.Iban, balance.AvailableAmount, balance.CurrentAmount, balance.Overdraft)
        });
    }
}
```

- [ ] **Step 3: Build to verify it compiles**

Run: `dotnet build src/MobileApp/MobileApp.csproj`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add src/MobileApp/Views/DataView.axaml src/MobileApp/Views/DataView.axaml.cs
git commit -m "Push TransactionsView when a balance card is tapped"
```

---

### Task 5: End-to-end manual verification

**Files:** none (no code changes — this task only runs the app).

**Interfaces:** none.

- [ ] **Step 1: Run the desktop app**

Run: `dotnet run --project src/MobileApp.Desktop/MobileApp.Desktop.csproj` (adjust the project path if the desktop head has a different name — check with `find src -iname "*.Desktop.csproj"` first).

- [ ] **Step 2: Get balances on screen**

In the running app, go to the "Data" tab. If no accounts are linked yet, use the existing "add account"/refresh flow already in `DataView` to populate `Balances` (this is unchanged pre-existing behavior, not part of this feature).

- [ ] **Step 3: Verify the push**

Tap a balance card. Expected: the view transitions to the Transactions placeholder page, showing that account's IBAN and available amount, with a back button visible in the navigation bar.

- [ ] **Step 4: Verify the pop**

Tap the back button. Expected: the view returns to the same balances list (still populated, unchanged).

- [ ] **Step 5: Repeat for a different account**

If more than one account is linked, tap a different balance card. Expected: the Transactions page now shows that second account's IBAN/amount, confirming the flow isn't stuck on stale data from the first tap.

No commit for this task — it's verification only. If any step fails, treat it as a bug against the relevant task above (do not silently patch around it without understanding why the earlier task's code didn't produce the expected behavior).
