using System;
using System.Diagnostics.Tracing;
using MobileApp.Debug;

namespace MobileApp;

internal sealed class OtelDiagnosticsListener(IDebugTelemetryStatus telemetryStatus) : EventListener
{
    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name.StartsWith("OpenTelemetry", StringComparison.Ordinal))
            EnableEvents(source, EventLevel.Verbose);
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        string msg;
        try
        {
            msg = e.Payload is { Count: > 0 }
                ? string.Format(e.Message ?? string.Empty, [.. e.Payload])
                : e.Message ?? e.EventName ?? "(no message)";
        }
        catch (FormatException)
        {
            msg = e.Message ?? e.EventName ?? "(no message)";
        }
        Console.WriteLine($"[OTel/{e.Level}] {msg}");

        switch (e.EventName)
        {
            case "ExportSuccess":
                telemetryStatus.RecordSuccess(DateTimeOffset.UtcNow);
                break;
            case "ExportFailure":
                telemetryStatus.RecordFailure(DateTimeOffset.UtcNow, msg);
                break;
        }
    }
}
