using System.Collections.Generic;

namespace MobileApp.Debug;

internal sealed class BoundedBuffer<T>(int capacity)
{
    private readonly object _lock = new();
    private readonly Queue<T> _items = new(capacity);

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_items.Count == capacity)
            {
                _items.Dequeue();
            }
            _items.Enqueue(item);
        }
    }

    public IReadOnlyList<T> Snapshot()
    {
        lock (_lock)
        {
            return _items.ToArray();
        }
    }
}
