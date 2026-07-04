using System.Collections.Generic;

namespace MobileApp.Debug;

public interface IDebugLogStore
{
    void Add(DebugLogEntry entry);
    IReadOnlyList<DebugLogEntry> Snapshot();
}

public sealed class DebugLogStore : IDebugLogStore
{
    private const int Capacity = 500;
    private readonly BoundedBuffer<DebugLogEntry> _buffer = new(Capacity);

    public void Add(DebugLogEntry entry) => _buffer.Add(entry);

    public IReadOnlyList<DebugLogEntry> Snapshot() => _buffer.Snapshot();
}
