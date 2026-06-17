using WorldMonitor.Caching;
using WorldMonitor.Caching.Tests.Fakes;
using Xunit;

namespace WorldMonitor.Caching.Tests;

public class BoundedExpiringMapTests
{
    [Fact]
    public void Get_returns_value_before_expiry_and_misses_after()
    {
        var clock = new TestClock();
        var map = new BoundedExpiringMap<int>(clock, maxEntries: 10);
        map.Set("k", 42, TimeSpan.FromSeconds(5));

        Assert.True(map.TryGet("k", out var v));
        Assert.Equal(42, v);

        clock.Advance(TimeSpan.FromSeconds(6));
        Assert.False(map.TryGet("k", out _));
    }

    [Fact]
    public void Set_evicts_oldest_when_over_capacity()
    {
        var clock = new TestClock();
        var map = new BoundedExpiringMap<int>(clock, maxEntries: 2);
        map.Set("a", 1, TimeSpan.FromMinutes(1));
        map.Set("b", 2, TimeSpan.FromMinutes(1));
        map.Set("c", 3, TimeSpan.FromMinutes(1)); // evicts "a" (oldest)

        Assert.False(map.TryGet("a", out _));
        Assert.True(map.TryGet("b", out _));
        Assert.True(map.TryGet("c", out _));
    }

    [Fact]
    public void Reset_existing_key_refreshes_order_and_value()
    {
        var clock = new TestClock();
        var map = new BoundedExpiringMap<int>(clock, maxEntries: 2);
        map.Set("a", 1, TimeSpan.FromMinutes(1));
        map.Set("b", 2, TimeSpan.FromMinutes(1));
        map.Set("a", 9, TimeSpan.FromMinutes(1)); // "a" becomes newest
        map.Set("c", 3, TimeSpan.FromMinutes(1)); // evicts "b" (now oldest)

        Assert.True(map.TryGet("a", out var a));
        Assert.Equal(9, a);
        Assert.False(map.TryGet("b", out _));
    }
}
