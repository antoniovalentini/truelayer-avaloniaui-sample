using System;
using MobileApp.Debug;
using Xunit;

namespace MobileApp.Tests;

public class BoundedBufferTests
{
    [Fact]
    public void Constructor_WithNonPositiveCapacity_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedBuffer<int>(0));
    }

    [Fact]
    public void Add_WithinCapacity_ReturnsAllItemsInOrder()
    {
        var buffer = new BoundedBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);

        Assert.Equal(new[] { 1, 2 }, buffer.Snapshot());
    }

    [Fact]
    public void Add_BeyondCapacity_EvictsOldestFirst()
    {
        var buffer = new BoundedBuffer<int>(3);
        buffer.Add(1);
        buffer.Add(2);
        buffer.Add(3);
        buffer.Add(4);

        Assert.Equal(new[] { 2, 3, 4 }, buffer.Snapshot());
    }
}
