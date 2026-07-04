using System;

namespace MobileApp.ViewModels;

public partial class DebugDeviceInfoViewModel : ViewModelBase
{
    public string AppVersion { get; } = App.Instance.AppVersion;
    public string DeviceId { get; } = App.Instance.DeviceId;
    public string DeviceName { get; } = App.Instance.DeviceName;
    public string DeviceType { get; } = App.Instance.DeviceType;
    public string RuntimeVersion { get; } = Environment.Version.ToString();
}
