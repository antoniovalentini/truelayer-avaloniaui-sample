using System;
using System.Collections.Generic;

namespace MobileApp.Debug;

internal sealed class BoundedBuffer<T>
{
    private readonly int _capacity;
    private readonly object _lock = new();
    private readonly Queue<T> _items;

    public BoundedBuffer(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0);
        _capacity = capacity;
        _items = new Queue<T>(capacity);
    }

    public void Add(T item)
    {
        lock (_lock)
        {
            if (_items.Count == _capacity)
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
