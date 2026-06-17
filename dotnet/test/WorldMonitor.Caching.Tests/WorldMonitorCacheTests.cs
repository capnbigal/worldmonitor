using WorldMonitor.Caching;
using WorldMonitor.Caching.Tests.Fakes;
using Xunit;

namespace WorldMonitor.Caching.Tests;

public class WorldMonitorCacheTests
{
    private sealed record Doc(string V);

    private static (WorldMonitorCache cache, InMemoryCacheStore store, TestClock clock) New()
    {
        var clock = new TestClock();
        var store = new InMemoryCacheStore(clock);
        return (new WorldMonitorCache(store, clock), store, clock);
    }

    [Fact]
    public async Task Miss_runs_fetcher_and_stores_result()
    {
        var (cache, store, _) = New();
        var result = await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), _ => Task.FromResult<Doc?>(new Doc("hi")));
        Assert.Equal("hi", result!.V);
        Assert.Equal(1, store.UpsertCount);
    }

    [Fact]
    public async Task Second_call_is_served_from_store_without_refetch()
    {
        var (cache, store, _) = New();
        var calls = 0;
        Func<CancellationToken, Task<Doc?>> fetch = _ => { calls++; return Task.FromResult<Doc?>(new Doc("v")); };

        await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch);
        var second = await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch);

        Assert.Equal("v", second!.V);
        Assert.Equal(1, calls);          // fetcher ran once
        Assert.Equal(1, store.UpsertCount);
    }

    [Fact]
    public async Task Expired_store_entry_triggers_refetch()
    {
        var (cache, store, clock) = New();
        var calls = 0;
        Func<CancellationToken, Task<Doc?>> fetch = _ => { calls++; return Task.FromResult<Doc?>(new Doc("v")); };

        await cache.GetOrSetAsync("k", TimeSpan.FromSeconds(5), fetch);
        clock.Advance(TimeSpan.FromSeconds(6));
        await cache.GetOrSetAsync("k", TimeSpan.FromSeconds(5), fetch);

        Assert.Equal(2, calls);
    }
}
