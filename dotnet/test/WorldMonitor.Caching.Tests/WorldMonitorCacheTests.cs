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

    [Fact]
    public async Task Concurrent_misses_for_same_key_run_fetcher_once()
    {
        var (cache, _, _) = New();
        var calls = 0;
        var gate = new TaskCompletionSource();
        Func<CancellationToken, Task<Doc?>> fetch = async _ =>
        {
            Interlocked.Increment(ref calls);
            await gate.Task;                 // hold all callers inside the single fetch
            return new Doc("v");
        };

        var callers = Enumerable.Range(0, 12)
            .Select(_ => cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch)).ToArray();
        gate.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, calls);                               // exactly one upstream fetch
        Assert.All(results, r => Assert.Equal("v", r!.V));
    }

    [Fact]
    public async Task Null_result_caches_sentinel_for_default_120s_and_returns_null()
    {
        var (cache, store, clock) = New();
        var calls = 0;
        Func<CancellationToken, Task<Doc?>> fetch = _ => { calls++; return Task.FromResult<Doc?>(null); };

        Assert.Null(await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch));
        Assert.Equal(1, store.UpsertCount);                  // sentinel stored

        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.Null(await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch));
        Assert.Equal(1, calls);                              // still within negative TTL ⇒ no refetch

        clock.Advance(TimeSpan.FromSeconds(2));              // now > 120s
        Assert.Null(await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), fetch));
        Assert.Equal(2, calls);                              // sentinel expired ⇒ refetch
    }

    [Fact]
    public async Task Fetcher_throw_caches_sentinel_for_30s_and_rethrows()
    {
        var (cache, store, clock) = New();
        Func<CancellationToken, Task<Doc?>> boom = _ => throw new InvalidOperationException("upstream down");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), boom));   // re-thrown to caller
        Assert.Equal(1, store.UpsertCount);                  // 30s sentinel stored

        // Within 30s a follower sees the sentinel ⇒ null, no fetch.
        Assert.Null(await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), _ => Task.FromResult<Doc?>(new Doc("late"))));

        clock.Advance(TimeSpan.FromSeconds(31));
        Assert.Equal("late2", (await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5),
            _ => Task.FromResult<Doc?>(new Doc("late2"))))!.V);              // sentinel expired ⇒ refetch
    }

    [Fact]
    public async Task Store_read_error_with_active_negative_cooldown_returns_null_without_fetching()
    {
        var (cache, store, _) = New();
        Func<CancellationToken, Task<Doc?>> nullFetch = _ => Task.FromResult<Doc?>(null);
        await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), nullFetch);   // arms local negative cooldown (120s)

        store.FailReads = true;                                               // simulate store outage
        var calls = 0;
        var r = await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5),
            _ => { calls++; return Task.FromResult<Doc?>(new Doc("x")); });
        Assert.Null(r);
        Assert.Equal(0, calls);                                              // cooldown short-circuits the fetch
    }

    [Fact]
    public async Task Store_write_failure_arms_positive_fallback_served_on_next_read_error()
    {
        var (cache, store, _) = New();
        store.FailWrites = true;                          // value can't persist to the store
        var first = await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), _ => Task.FromResult<Doc?>(new Doc("v")));
        Assert.Equal("v", first!.V);                      // caller still gets the value

        store.FailReads = true;                           // now the store read also fails
        var calls = 0;
        var second = await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5),
            _ => { calls++; return Task.FromResult<Doc?>(new Doc("other")); });

        Assert.Equal("v", second!.V);                     // served from the isolate-local positive fallback
        Assert.Equal(0, calls);                           // no refetch needed
    }

    [Fact]
    public async Task Positive_fallback_expires_after_30s_cap()
    {
        var (cache, store, clock) = New();
        store.FailWrites = true;
        await cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5), _ => Task.FromResult<Doc?>(new Doc("v")));
        store.FailReads = true;

        clock.Advance(TimeSpan.FromSeconds(31));           // beyond the 30s outage-positive cap
        var calls = 0;
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetOrSetAsync<Doc>("k", TimeSpan.FromMinutes(5),
            _ => { calls++; throw new InvalidOperationException("still down"); }));
        Assert.Equal(1, calls);                            // fallback expired ⇒ fetch attempted (and threw)
    }

    [Fact]
    public async Task Fetcher_exceeding_timeout_throws_TimeoutException_and_caches_sentinel()
    {
        var (cache, store, _) = New();
        Func<CancellationToken, Task<Doc?>> slow = async ct => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return new Doc("late"); };

        await Assert.ThrowsAsync<TimeoutException>(() => cache.GetOrSetAsync(
            "k", TimeSpan.FromMinutes(5), slow, fetcherTimeout: TimeSpan.FromMilliseconds(50)));

        Assert.Equal(1, store.UpsertCount);   // a 30s error-sentinel was written before re-throw
    }
}
