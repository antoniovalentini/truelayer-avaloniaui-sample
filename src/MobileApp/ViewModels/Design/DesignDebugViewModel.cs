using Microsoft.Extensions.Configuration;
using MobileApp.Debug;
using MobileApp.Fakes;

namespace MobileApp.ViewModels.Design;

// ponytail: design-time only (Avalonia previewer Design.DataContext), never constructed at
// runtime — stores are real (cheap, parameterless) rather than fakes since none exist yet for them.
public class DesignDebugViewModel() : DebugViewModel(
    new DebugLogsViewModel(new DebugStore<DebugLogEntry>(500)),
    new DebugNetworkViewModel(new DebugStore<DebugNetworkEntry>(100)),
    new DebugTokensViewModel(new FakeAuthTokenStorage()),
    new DebugDeviceInfoViewModel(new DebugTelemetryStatus(), new ConfigurationBuilder().Build()),
    new DebugStorageViewModel(new FakeAuthTokenStorage()),
    new DebugExportService(new DebugStore<DebugLogEntry>(500), new DebugStore<DebugNetworkEntry>(100), new DebugTelemetryStatus()));
