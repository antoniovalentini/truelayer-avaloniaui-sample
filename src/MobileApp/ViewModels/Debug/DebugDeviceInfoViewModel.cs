using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Configuration;
using MobileApp.Debug;

namespace MobileApp.ViewModels;

public partial class DebugDeviceInfoViewModel : ViewModelBase
{
    private readonly IDebugTelemetryStatus _telemetryStatus;

    public DebugDeviceInfoViewModel(IDebugTelemetryStatus telemetryStatus, IConfiguration configuration)
    {
        _telemetryStatus = telemetryStatus;
        HoneycombEndpoint = configuration["Honeycomb:Endpoint"] ?? "(not configured)";
        RefreshTelemetry();
    }

    public string AppVersion { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "design" : App.Instance.AppVersion;
    public string DeviceId { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "design-device-id" : App.Instance.DeviceId;
    public string DeviceName { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "Design Device" : App.Instance.DeviceName;
    public string DeviceType { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "Design OS" : App.Instance.DeviceType;
    public string RuntimeVersion { get; } = Environment.Version.ToString();

    public string HoneycombEndpoint { get; }

    [ObservableProperty] private string _lastSuccessDisplay = "none";
    [ObservableProperty] private string _lastFailureDisplay = "none";

    [RelayCommand]
    private void RefreshTelemetry()
    {
        var snapshot = _telemetryStatus.Snapshot();
        LastSuccessDisplay = snapshot.LastSuccessAt?.ToString("u") ?? "none";
        LastFailureDisplay = snapshot.LastFailureAt is { } failedAt
            ? $"{failedAt:u} — {snapshot.LastFailureMessage}"
            : "none";
    }
}
