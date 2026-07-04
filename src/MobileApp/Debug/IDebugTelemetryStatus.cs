using System;

namespace MobileApp.Debug;

public sealed record TelemetryStatusSnapshot(DateTimeOffset? LastSuccessAt, DateTimeOffset? LastFailureAt, string? LastFailureMessage);

public interface IDebugTelemetryStatus
{
    TelemetryStatusSnapshot Snapshot();
    void RecordSuccess(DateTimeOffset timestamp);
    void RecordFailure(DateTimeOffset timestamp, string message);
}

public sealed class DebugTelemetryStatus : IDebugTelemetryStatus
{
    private readonly object _lock = new();
    private DateTimeOffset? _lastSuccessAt;
    private DateTimeOffset? _lastFailureAt;
    private string? _lastFailureMessage;

    public TelemetryStatusSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new TelemetryStatusSnapshot(_lastSuccessAt, _lastFailureAt, _lastFailureMessage);
        }
    }

    public void RecordSuccess(DateTimeOffset timestamp)
    {
        lock (_lock) { _lastSuccessAt = timestamp; }
    }

    public void RecordFailure(DateTimeOffset timestamp, string message)
    {
        lock (_lock)
        {
            _lastFailureAt = timestamp;
            _lastFailureMessage = message;
        }
    }
}
