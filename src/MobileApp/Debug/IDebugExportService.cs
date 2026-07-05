using System;
using System.Linq;
using System.Text;

namespace MobileApp.Debug;

public interface IDebugExportService
{
    string BuildDiagnosticsBundle();
}

public sealed class DebugExportService(
    IDebugStore<DebugLogEntry> logStore,
    IDebugStore<DebugNetworkEntry> networkStore,
    IDebugTelemetryStatus telemetryStatus) : IDebugExportService
{
    public string BuildDiagnosticsBundle()
    {
        var sb = new StringBuilder();

        sb.AppendLine($"TrueMobile diagnostics bundle — {DateTimeOffset.UtcNow:u}");
        sb.AppendLine($"App version: {App.Instance.AppVersion}");
        sb.AppendLine($"Device: {App.Instance.DeviceName} ({App.Instance.DeviceType})");
        sb.AppendLine();

        var telemetry = telemetryStatus.Snapshot();
        sb.AppendLine("== Telemetry ==");
        sb.AppendLine($"Last success: {telemetry.LastSuccessAt?.ToString("u") ?? "none"}");
        sb.AppendLine(telemetry.LastFailureAt is { } failedAt
            ? $"Last failure: {failedAt:u} {telemetry.LastFailureMessage}"
            : "Last failure: none");
        sb.AppendLine();

        sb.AppendLine("== Network (most recent first) ==");
        foreach (var entry in networkStore.Snapshot().Reverse())
        {
            sb.AppendLine($"[{entry.Timestamp:u}] {entry.Method} {entry.Uri} -> {entry.StatusCode?.ToString() ?? "ERROR"} ({entry.DurationMs}ms) {entry.Error}");
        }
        sb.AppendLine();

        sb.AppendLine("== Logs (most recent first) ==");
        foreach (var entry in logStore.Snapshot().Reverse())
        {
            sb.AppendLine($"[{entry.Timestamp:u}] [{entry.Level}] {entry.Category}: {entry.Message}");
        }

        return sb.ToString();
    }
}
