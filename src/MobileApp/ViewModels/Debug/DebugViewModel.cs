using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugViewModel : ViewModelBase
{
    private readonly DebugLogsViewModel _logsViewModel;
    private readonly DebugNetworkViewModel _networkViewModel;
    private readonly DebugTokensViewModel _tokensViewModel;
    private readonly DebugDeviceInfoViewModel _deviceInfoViewModel;
    private readonly DebugStorageViewModel _storageViewModel;
    private readonly IDebugExportService _exportService;

    public DebugViewModel(
        DebugLogsViewModel logsViewModel,
        DebugNetworkViewModel networkViewModel,
        DebugTokensViewModel tokensViewModel,
        DebugDeviceInfoViewModel deviceInfoViewModel,
        DebugStorageViewModel storageViewModel,
        IDebugExportService exportService)
    {
        _logsViewModel = logsViewModel;
        _networkViewModel = networkViewModel;
        _tokensViewModel = tokensViewModel;
        _deviceInfoViewModel = deviceInfoViewModel;
        _storageViewModel = storageViewModel;
        _exportService = exportService;
    }

    [ObservableProperty] private ViewModelBase? _currentSubViewModel;

    [RelayCommand] private void OpenLogs() => CurrentSubViewModel = _logsViewModel;
    [RelayCommand] private void OpenNetwork() => CurrentSubViewModel = _networkViewModel;
    [RelayCommand] private void OpenTokens() => CurrentSubViewModel = _tokensViewModel;
    [RelayCommand] private void OpenDeviceInfo() => CurrentSubViewModel = _deviceInfoViewModel;
    [RelayCommand] private void OpenStorage() => CurrentSubViewModel = _storageViewModel;
    [RelayCommand] private void GoBack() => CurrentSubViewModel = null;

    public string BuildDiagnosticsBundle() => _exportService.BuildDiagnosticsBundle();
}
