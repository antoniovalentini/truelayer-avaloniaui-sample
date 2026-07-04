using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MobileApp.ViewModels;

public partial class MainViewModel : ViewModelBase
{
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

        DataButtonFontWeight = SelectedFontWeight;
        PaymentsButtonFontWeight = DefaultFontWeight;
        SettingsButtonFontWeight = DefaultFontWeight;

        DataButtonForeground = _selectedButtonForeground;
        PaymentsButtonForeground = _defaultButtonForeground;
        SettingsButtonForeground = _defaultButtonForeground;
    }

    private Thickness _safeArea = new(0);
    public Thickness SafeArea
    {
        get => _safeArea;
        set
        {
            _safeArea = value;
            OnPropertyChanged();
        }
    }

    public void OnSelectionChanged(object? sender, PageSelectionChangedEventArgs e)
    {
        if (e.CurrentPage?.Header?.ToString() is not { } header) return;

        DataButtonFontWeight = header == "Data" ? SelectedFontWeight : DefaultFontWeight;
        PaymentsButtonFontWeight = header == "Payments" ? SelectedFontWeight : DefaultFontWeight;
        SettingsButtonFontWeight = header == "Settings" ? SelectedFontWeight : DefaultFontWeight;

        DataButtonForeground = header == "Data" ? _selectedButtonForeground : _defaultButtonForeground;
        PaymentsButtonForeground = header == "Payments" ? _selectedButtonForeground : _defaultButtonForeground;
        SettingsButtonForeground = header == "Settings" ? _selectedButtonForeground : _defaultButtonForeground;
    }

    private const FontWeight SelectedFontWeight = FontWeight.Bold;
    private const FontWeight DefaultFontWeight = FontWeight.SemiBold;

    [ObservableProperty] private FontWeight _dataButtonFontWeight = DefaultFontWeight;
    [ObservableProperty] private FontWeight _paymentsButtonFontWeight = DefaultFontWeight;
    [ObservableProperty] private FontWeight _settingsButtonFontWeight = DefaultFontWeight;

    private readonly IBrush _selectedButtonForeground = Brushes.White;
    private readonly IBrush _defaultButtonForeground = Brushes.Black;

    [ObservableProperty] private IBrush _dataButtonForeground;
    [ObservableProperty] private IBrush _paymentsButtonForeground;
    [ObservableProperty] private IBrush _settingsButtonForeground;
}
