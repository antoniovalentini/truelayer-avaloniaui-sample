using System.Collections.Generic;

namespace MobileApp.Debug;

public interface IDebugNetworkStore
{
    void Add(DebugNetworkEntry entry);
    IReadOnlyList<DebugNetworkEntry> Snapshot();
}

public sealed class DebugNetworkStore : IDebugNetworkStore
{
    private const int Capacity = 100;
    private readonly BoundedBuffer<DebugNetworkEntry> _buffer = new(Capacity);

    public void Add(DebugNetworkEntry entry) => _buffer.Add(entry);

    public IReadOnlyList<DebugNetworkEntry> Snapshot() => _buffer.Snapshot();
}
