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
