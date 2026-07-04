using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugTelemetryViewModel : ViewModelBase
{
    private readonly IDebugTelemetryStatus _status;

    public DebugTelemetryViewModel(IDebugTelemetryStatus status, IConfiguration configuration)
    {
        _status = status;
        HoneycombEndpoint = configuration["Honeycomb:Endpoint"] ?? "(not configured)";
        Refresh();
    }

    public string HoneycombEndpoint { get; }

    [ObservableProperty] private string _lastSuccessDisplay = "none";
    [ObservableProperty] private string _lastFailureDisplay = "none";

    [RelayCommand]
    private void Refresh()
    {
        var snapshot = _status.Snapshot();
        LastSuccessDisplay = snapshot.LastSuccessAt?.ToString("u") ?? "none";
        LastFailureDisplay = snapshot.LastFailureAt is { } failedAt
            ? $"{failedAt:u} — {snapshot.LastFailureMessage}"
            : "none";
    }
}
