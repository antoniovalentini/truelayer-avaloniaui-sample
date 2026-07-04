using System;

namespace MobileApp.ViewModels;

public partial class DebugDeviceInfoViewModel : ViewModelBase
{
    public string AppVersion { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "design" : App.Instance.AppVersion;
    public string DeviceId { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "design-device-id" : App.Instance.DeviceId;
    public string DeviceName { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "Design Device" : App.Instance.DeviceName;
    public string DeviceType { get; } = global::Avalonia.Controls.Design.IsDesignMode ? "Design OS" : App.Instance.DeviceType;
    public string RuntimeVersion { get; } = Environment.Version.ToString();
}
