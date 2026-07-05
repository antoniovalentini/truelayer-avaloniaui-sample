using System.Collections.Generic;

namespace MobileApp.Debug;

public interface IDebugStore<T>
{
    void Add(T entry);
    IReadOnlyList<T> Snapshot();
}

public sealed class DebugStore<T>(int capacity) : IDebugStore<T>
{
    private readonly BoundedBuffer<T> _buffer = new(capacity);

    public void Add(T entry) => _buffer.Add(entry);

    public IReadOnlyList<T> Snapshot() => _buffer.Snapshot();
}
