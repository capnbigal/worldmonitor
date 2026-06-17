# P1a-2 — Cache Wrapper (`WorldMonitorCache`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the read-through cache wrapper `WorldMonitorCache` that sits over the P1a-1 `ICacheStore`, faithfully reproducing the legacy `cachedFetchJson` semantics — three *separate* storm guards (in-flight coalescing, asymmetric negative-sentinel caching, and an isolate-local outage bridge) — entirely with deterministic, database-free unit tests.

**Architecture:** A thin hand-rolled wrapper, **not** FusionCache. The legacy `server/_shared/redis.ts#cachedFetchJson` **re-throws** on fetcher error (it does not serve last-good), which is the opposite of FusionCache's fail-safe-on-factory-error — so a library would fight the contract. Instead `WorldMonitorCache` orchestrates: read `ICacheStore` (tri-state) → on hit translate the `__WM_NEG__` sentinel to `null` → on miss/error consult an in-process positive-fallback and negative-cooldown → coalesce concurrent misses through a single in-flight task → run the fetcher under a hard timeout → write the value (or a sentinel with asymmetric TTL) back to the store. All time comes from the P1a-1 `IClock`, so expiry/eviction are deterministic in tests. The store is faked in-memory, so every test is fast and DB-free.

**Tech Stack:** .NET 10, C# 13, `System.Text.Json` (the P0 `WmJson.Options`), xUnit. New project `WorldMonitor.Caching` references `WorldMonitor.Data` (for `ICacheStore`/`IClock`/`CacheUpsert`) and `WorldMonitor.Contracts` (for `WmJson`). **No external cache package.**

**This is P1a-2 of the program roadmap** (phase P1). Builds on P1a-1 (`feat/dotnet-p1a-cache-substrate`, PR #2) which it stacks on.

**Faithful to the legacy reference** `server/_shared/redis.ts` (verified): `NEG_SENTINEL='__WM_NEG__'`; `cachedFetchJson(key, ttlSeconds, fetcher, negativeTtlSeconds=120, opts?)`; null ⇒ store sentinel for `negativeTtl` (default 120s); fetcher throw/timeout ⇒ store sentinel for `min(negativeTtl, 30s)` **and re-throw**; `FETCHER_TIMEOUT_MS_DEFAULT=30_000`; in-process `localNegativeUntil` + `localPositiveFallback` maps capped at `LOCAL_FALLBACK_MAX_ENTRIES=5000`; outage positive bridge TTL `min(ttl, 30s)`.

**Explicitly deferred (so reviewers don't flag as gaps):**
- The `cachedFetchJsonWithMeta` variant (`source`/`leader` telemetry) → **P2** (usage-event emission belongs with the gateway).
- The `hasRemoteRedisConfig()` sidecar-vs-cloud distinction → dropped (the SQL store is the single durable store; the outage bridge arms on read-error or write-failure).
- HTTP cache-tier → TTL mapping, key-prefixing → **P2**. The wrapper takes an explicit per-call TTL.
- DI registration (`AddWorldMonitorCache`) → folded into the API host wiring in **P2**.

---

## Prerequisites

- .NET 10 SDK (verified). LocalDB not required — P1a-2 tests are pure unit tests with an in-memory store fake.
- Branch: from `feat/dotnet-p1a-cache-substrate` (P1a-1, not merged), create `feat/dotnet-p1a2-cache-wrapper` (stacks on P1a-1, which stacks on P0).

## File structure

```
dotnet/
  src/WorldMonitor.Caching/
    WorldMonitor.Caching.csproj            ref WorldMonitor.Data + WorldMonitor.Contracts
    CacheConstants.cs                       sentinel + TTL/cap constants (mirrored from redis.ts)
    IWorldMonitorCache.cs                   the GetOrSetAsync surface
    BoundedExpiringMap.cs                   IClock-driven FIFO+expiry+cap map (negative + positive caches)
    WorldMonitorCache.cs                    the read-through wrapper (3 storm guards)
  test/WorldMonitor.Caching.Tests/
    WorldMonitor.Caching.Tests.csproj
    Fakes/TestClock.cs                      settable IClock
    Fakes/InMemoryCacheStore.cs             ICacheStore fake (controllable FailReads/FailWrites + UpsertCount)
    BoundedExpiringMapTests.cs
    WorldMonitorCacheTests.cs               hit/miss/store, negative, coalescing, outage, timeout
```

---

### Task 1: Scaffold `WorldMonitor.Caching` + test project

**Files:**
- Create: `dotnet/src/WorldMonitor.Caching/WorldMonitor.Caching.csproj`
- Create: `dotnet/test/WorldMonitor.Caching.Tests/WorldMonitor.Caching.Tests.csproj`

- [ ] **Step 1: Branch** (stacked on P1a-1)

Run:
```bash
git checkout feat/dotnet-p1a-cache-substrate && git checkout -b feat/dotnet-p1a2-cache-wrapper
```
Expected: `Switched to a new branch 'feat/dotnet-p1a2-cache-wrapper'`

- [ ] **Step 2: Create projects + references**

Run:
```bash
dotnet new classlib -n WorldMonitor.Caching -o dotnet/src/WorldMonitor.Caching -f net10.0
dotnet new xunit -n WorldMonitor.Caching.Tests -o dotnet/test/WorldMonitor.Caching.Tests -f net10.0
rm dotnet/src/WorldMonitor.Caching/Class1.cs dotnet/test/WorldMonitor.Caching.Tests/UnitTest1.cs
dotnet sln dotnet/WorldMonitor.slnx add dotnet/src/WorldMonitor.Caching dotnet/test/WorldMonitor.Caching.Tests
dotnet add dotnet/src/WorldMonitor.Caching reference dotnet/src/WorldMonitor.Data dotnet/src/WorldMonitor.Contracts
dotnet add dotnet/test/WorldMonitor.Caching.Tests reference dotnet/src/WorldMonitor.Caching dotnet/src/WorldMonitor.Data
```
Expected: all succeed.

- [ ] **Step 3: Build empty**

Run: `dotnet build dotnet/WorldMonitor.slnx`
Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add dotnet/
git commit -m "chore(caching): scaffold WorldMonitor.Caching + test project"
```

---

### Task 2: `CacheConstants` + `IWorldMonitorCache`

**Files:**
- Create: `dotnet/src/WorldMonitor.Caching/CacheConstants.cs`
- Create: `dotnet/src/WorldMonitor.Caching/IWorldMonitorCache.cs`

- [ ] **Step 1: Implement** (no test — declarations exercised from Task 5 on)

Create `CacheConstants.cs` (values mirrored from `server/_shared/redis.ts`):
```csharp
namespace WorldMonitor.Caching;

public static class CacheConstants
{
    /// <summary>Marker stored as the cache value to represent a cached "no data" result.</summary>
    public const string NegativeSentinel = "__WM_NEG__";

    /// <summary>Default negative-cache TTL when a fetcher returns null (legacy negativeTtlSeconds=120).</summary>
    public static readonly TimeSpan DefaultNegativeTtl = TimeSpan.FromSeconds(120);

    /// <summary>Negative TTL cap when a fetcher throws/times out (legacy FETCH_ERROR_NEGATIVE_TTL_SECONDS=30).</summary>
    public static readonly TimeSpan FetchErrorNegativeTtl = TimeSpan.FromSeconds(30);

    /// <summary>TTL cap for the isolate-local positive outage bridge (legacy REDIS_FAILURE_POSITIVE_TTL_SECONDS=30).</summary>
    public static readonly TimeSpan OutagePositiveTtl = TimeSpan.FromSeconds(30);

    /// <summary>Default hard upper bound on a single fetcher (legacy FETCHER_TIMEOUT_MS_DEFAULT=30_000).</summary>
    public static readonly TimeSpan DefaultFetcherTimeout = TimeSpan.FromSeconds(30);

    /// <summary>FIFO cap on each in-process fallback map (legacy LOCAL_FALLBACK_MAX_ENTRIES=5000).</summary>
    public const int LocalFallbackMaxEntries = 5000;
}
```

Create `IWorldMonitorCache.cs`:
```csharp
namespace WorldMonitor.Caching;

public interface IWorldMonitorCache
{
    /// <summary>Read-through cache. Returns the cached value, runs <paramref name="fetcher"/> on miss
    /// (concurrent misses coalesce into one run), caches the result for <paramref name="ttl"/>, and
    /// caches a negative sentinel when the fetcher returns null (<paramref name="negativeTtl"/>, default 120s)
    /// or throws (capped at 30s, then the exception is re-thrown). Returns null for a negative/empty result.</summary>
    Task<T?> GetOrSetAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T?>> fetcher,
        TimeSpan? negativeTtl = null,
        TimeSpan? fetcherTimeout = null,
        CancellationToken ct = default) where T : class;
}
```

- [ ] **Step 2: Build + commit**

Run: `dotnet build dotnet/src/WorldMonitor.Caching`
Expected: 0 errors.
```bash
git add dotnet/
git commit -m "feat(caching): add CacheConstants + IWorldMonitorCache surface"
```

---

### Task 3: `BoundedExpiringMap<TValue>` (IClock FIFO + expiry + cap)

Backs both the negative cooldown and the positive outage fallback. Deterministic via `IClock`.

**Files:**
- Create: `dotnet/src/WorldMonitor.Caching/BoundedExpiringMap.cs`
- Test: `dotnet/test/WorldMonitor.Caching.Tests/BoundedExpiringMapTests.cs`
- Create: `dotnet/test/WorldMonitor.Caching.Tests/Fakes/TestClock.cs`

- [ ] **Step 1: Test clock fake**

Create `Fakes/TestClock.cs`:
```csharp
using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching.Tests.Fakes;

public sealed class TestClock : IClock
{
    public DateTime UtcNow { get; private set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public void Advance(TimeSpan by) => UtcNow += by;
}
```

- [ ] **Step 2: Write the failing test**

Create `BoundedExpiringMapTests.cs`:
```csharp
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
```

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: FAIL — `BoundedExpiringMap` does not exist.

- [ ] **Step 4: Implement**

Create `BoundedExpiringMap.cs`:
```csharp
using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching;

/// <summary>Thread-safe map with absolute-expiry entries and FIFO eviction at a fixed cap.
/// Mirrors the legacy isolate-local fallback Maps; time is supplied by IClock for determinism.</summary>
public sealed class BoundedExpiringMap<TValue>(IClock clock, int maxEntries)
{
    private readonly object _gate = new();
    private readonly LinkedList<string> _order = new();
    private readonly Dictionary<string, (LinkedListNode<string> Node, DateTime ExpiresAt, TValue Value)> _map = new();

    public void Set(string key, TValue value, TimeSpan ttl)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var existing)) _order.Remove(existing.Node);
            var node = _order.AddLast(key);
            _map[key] = (node, clock.UtcNow + ttl, value);
            while (_map.Count > maxEntries)
            {
                var oldest = _order.First!;
                _order.RemoveFirst();
                _map.Remove(oldest.Value);
            }
        }
    }

    public bool TryGet(string key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out var e))
            {
                if (e.ExpiresAt > clock.UtcNow) { value = e.Value; return true; }
                _order.Remove(e.Node);
                _map.Remove(key);
            }
            value = default!;
            return false;
        }
    }
}
```

- [ ] **Step 5: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS (3 passed).
```bash
git add dotnet/
git commit -m "feat(caching): add IClock-driven BoundedExpiringMap (FIFO + expiry + cap)"
```

---

### Task 4: `InMemoryCacheStore` test fake

A controllable in-memory `ICacheStore` so the wrapper is tested without a database. Supports simulating store read errors / write failures and counts upserts.

**Files:**
- Create: `dotnet/test/WorldMonitor.Caching.Tests/Fakes/InMemoryCacheStore.cs`

- [ ] **Step 1: Implement** (test helper; exercised from Task 5)

Create `Fakes/InMemoryCacheStore.cs`:
```csharp
using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching.Tests.Fakes;

public sealed class InMemoryCacheStore(IClock clock) : ICacheStore
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string Value, DateTime Exp, DateTime? Fetched, int? Rc)> _data = new();

    public bool FailReads { get; set; }
    public bool FailWrites { get; set; }
    public int UpsertCount { get; private set; }

    public Task<CacheReadResult> ReadAsync(string key, CancellationToken ct = default)
    {
        if (FailReads) return Task.FromResult(CacheReadResult.Error);
        lock (_gate)
        {
            if (_data.TryGetValue(key, out var e) && e.Exp > clock.UtcNow)
                return Task.FromResult(CacheReadResult.Hit(e.Value, e.Exp, e.Fetched, e.Rc));
            return Task.FromResult(CacheReadResult.Miss);
        }
    }

    public Task UpsertAsync(CacheUpsert e, CancellationToken ct = default)
    {
        if (FailWrites) throw new InvalidOperationException("simulated write failure");
        lock (_gate)
        {
            _data[e.Key] = (e.Value, clock.UtcNow + e.Ttl, e.FetchedAt ?? clock.UtcNow, e.RecordCount);
            UpsertCount++;
        }
        return Task.CompletedTask;
    }

    public Task<bool> ExtendTtlAsync(string key, TimeSpan ttl, CancellationToken ct = default)
    {
        lock (_gate)
        {
            if (_data.TryGetValue(key, out var e) && e.Exp > clock.UtcNow)
            {
                _data[key] = (e.Value, clock.UtcNow + ttl, e.Fetched, e.Rc);
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> ReadManyAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = new Dictionary<string, string>();
            foreach (var k in keys)
                if (_data.TryGetValue(k, out var e) && e.Exp > clock.UtcNow) r[k] = e.Value;
            return Task.FromResult<IReadOnlyDictionary<string, string>>(r);
        }
    }

    public Task<IReadOnlyList<CachePresence>> ProbeAsync(IReadOnlyCollection<string> keys, CancellationToken ct = default)
    {
        lock (_gate)
        {
            var r = new List<CachePresence>();
            foreach (var k in keys)
                if (_data.TryGetValue(k, out var e) && e.Exp > clock.UtcNow)
                    r.Add(new CachePresence(k, e.Value.Length * 2, e.Exp));
            return Task.FromResult<IReadOnlyList<CachePresence>>(r);
        }
    }
}
```

- [ ] **Step 2: Build the test project**

Run: `dotnet build dotnet/test/WorldMonitor.Caching.Tests`
Expected: 0 errors.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(caching): add InMemoryCacheStore + TestClock fakes"
```

---

### Task 5: `WorldMonitorCache` — hit / miss / store (the happy paths)

**Files:**
- Create: `dotnet/src/WorldMonitor.Caching/WorldMonitorCache.cs`
- Test: `dotnet/test/WorldMonitor.Caching.Tests/WorldMonitorCacheTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `WorldMonitorCacheTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: FAIL — `WorldMonitorCache` does not exist.

- [ ] **Step 3: Implement** (full wrapper — later tasks add tests for the branches already coded here)

Create `WorldMonitorCache.cs`:
```csharp
using System.Collections.Concurrent;
using System.Text.Json;
using WorldMonitor.Contracts.Json;
using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Time;

namespace WorldMonitor.Caching;

/// <summary>Read-through cache over <see cref="ICacheStore"/> reproducing legacy cachedFetchJson semantics:
/// in-flight coalescing, asymmetric negative-sentinel caching, and an isolate-local outage bridge.</summary>
public sealed class WorldMonitorCache(ICacheStore store, IClock clock) : IWorldMonitorCache
{
    private readonly ConcurrentDictionary<string, Lazy<Task<object?>>> _inflight = new();
    private readonly BoundedExpiringMap<bool> _negative = new(clock, CacheConstants.LocalFallbackMaxEntries);
    private readonly BoundedExpiringMap<object> _positiveFallback = new(clock, CacheConstants.LocalFallbackMaxEntries);

    public async Task<T?> GetOrSetAsync<T>(
        string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> fetcher,
        TimeSpan? negativeTtl = null, TimeSpan? fetcherTimeout = null, CancellationToken ct = default) where T : class
    {
        var negTtl = negativeTtl ?? CacheConstants.DefaultNegativeTtl;

        var read = await store.ReadAsync(key, ct);
        if (read.Status == CacheReadStatus.Hit)
            return read.Value == CacheConstants.NegativeSentinel ? null : Deserialize<T>(read.Value!);

        if (_positiveFallback.TryGet(key, out var lp)) return (T)lp;

        var hadReadError = read.Status == CacheReadStatus.Error;
        if (hadReadError && _negative.TryGet(key, out _)) return null;

        var timeout = fetcherTimeout ?? CacheConstants.DefaultFetcherTimeout;
        var lazy = _inflight.GetOrAdd(key, k => new Lazy<Task<object?>>(
            () => FetchAndStoreAsync(k, ttl, fetcher, negTtl, timeout, hadReadError, ct),
            LazyThreadSafetyMode.ExecutionAndPublication));
        return (T?)await lazy.Value;
    }

    private async Task<object?> FetchAndStoreAsync<T>(
        string key, TimeSpan ttl, Func<CancellationToken, Task<T?>> fetcher,
        TimeSpan negTtl, TimeSpan timeout, bool hadReadError, CancellationToken ct) where T : class
    {
        try
        {
            var result = await WithTimeoutAsync(fetcher, key, timeout, ct);
            if (result is not null)
            {
                var wrote = await TryUpsertAsync(key, Serialize(result), ttl, ct);
                if (hadReadError || !wrote) _positiveFallback.Set(key, result, CacheConstants.OutagePositiveTtl);
                return result;
            }
            _negative.Set(key, true, negTtl);
            await TryUpsertAsync(key, CacheConstants.NegativeSentinel, negTtl, ct);
            return null;
        }
        catch
        {
            var seconds = Math.Max(1, Math.Min(negTtl.TotalSeconds, CacheConstants.FetchErrorNegativeTtl.TotalSeconds));
            var errTtl = TimeSpan.FromSeconds(seconds);
            _negative.Set(key, true, errTtl);
            await TryUpsertAsync(key, CacheConstants.NegativeSentinel, errTtl, ct);
            throw; // legacy cachedFetchJson re-throws on fetcher error (does NOT serve last-good)
        }
        finally
        {
            _inflight.TryRemove(key, out _);
        }
    }

    private async Task<bool> TryUpsertAsync(string key, string value, TimeSpan ttl, CancellationToken ct)
    {
        try
        {
            await store.UpsertAsync(new CacheUpsert(key, value, ttl, FetchedAt: clock.UtcNow), ct);
            return true;
        }
        catch
        {
            return false; // store write failure ⇒ arm the outage bridge instead
        }
    }

    private static async Task<T?> WithTimeoutAsync<T>(
        Func<CancellationToken, Task<T?>> fetcher, string key, TimeSpan timeout, CancellationToken ct) where T : class
    {
        var fetch = fetcher(ct); // pass the CALLER's token — the timeout does NOT cancel the fetcher (matches legacy)
        using var delayCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var delay = Task.Delay(timeout, delayCts.Token);
        if (await Task.WhenAny(fetch, delay) == delay && !fetch.IsCompleted)
            throw new TimeoutException($"cache fetcher timeout after {timeout.TotalMilliseconds}ms for \"{key}\"");
        await delayCts.CancelAsync(); // fetch won — stop the timer instead of leaking it until it fires
        return await fetch;           // observe the real result/exception
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, WmJson.Options);
    private static T Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, WmJson.Options)!;
}
```

- [ ] **Step 4: Run, verify pass; commit**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS (the 3 happy-path tests + the 3 BoundedExpiringMap tests).
```bash
git add dotnet/
git commit -m "feat(caching): WorldMonitorCache read-through (hit/miss/store)"
```

---

### Task 6: Coalescing — concurrent misses share one fetch

**Files:**
- Test: append to `dotnet/test/WorldMonitor.Caching.Tests/WorldMonitorCacheTests.cs`

- [ ] **Step 1: Write the test**

Add to `WorldMonitorCacheTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS. (The `Lazy<Task>` in `_inflight` guarantees a single fetcher invocation for concurrent callers.)

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(caching): prove concurrent-miss coalescing runs fetcher once"
```

---

### Task 7: Negative caching — null vs throw asymmetry + re-throw

**Files:**
- Test: append to `WorldMonitorCacheTests.cs`

- [ ] **Step 1: Write the tests**

Add to `WorldMonitorCacheTests.cs`:
```csharp
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
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(caching): prove negative-sentinel asymmetry (120s null / 30s throw) + re-throw + cooldown"
```

---

### Task 8: Outage bridge — positive fallback served on store read error

**Files:**
- Test: append to `WorldMonitorCacheTests.cs`

- [ ] **Step 1: Write the tests**

Add to `WorldMonitorCacheTests.cs`:
```csharp
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
        await Assert.ThrowsAnyAsync<Exception>(() => cache.GetOrSetAsync("k", TimeSpan.FromMinutes(5),
            _ => { calls++; throw new InvalidOperationException("still down"); }));
        Assert.Equal(1, calls);                            // fallback expired ⇒ fetch attempted (and threw)
    }
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(caching): prove isolate-local positive outage bridge (arm + serve + 30s expiry)"
```

---

### Task 9: Fetcher timeout

**Files:**
- Test: append to `WorldMonitorCacheTests.cs`

- [ ] **Step 1: Write the test** (uses a short real timeout — the only time-based test that can't use the virtual clock, since the timeout is a real `Task.Delay`)

Add to `WorldMonitorCacheTests.cs`:
```csharp
    [Fact]
    public async Task Fetcher_exceeding_timeout_throws_TimeoutException_and_caches_sentinel()
    {
        var (cache, store, _) = New();
        Func<CancellationToken, Task<Doc?>> slow = async ct => { await Task.Delay(TimeSpan.FromSeconds(5), ct); return new Doc("late"); };

        await Assert.ThrowsAsync<TimeoutException>(() => cache.GetOrSetAsync(
            "k", TimeSpan.FromMinutes(5), slow, fetcherTimeout: TimeSpan.FromMilliseconds(50)));

        Assert.Equal(1, store.UpsertCount);   // a 30s error-sentinel was written before re-throw
    }
```

- [ ] **Step 2: Run, verify pass**

Run: `dotnet test dotnet/test/WorldMonitor.Caching.Tests`
Expected: PASS (timeout fires at 50 ms; the test completes in well under a second).

- [ ] **Step 3: Commit**

```bash
git add dotnet/
git commit -m "test(caching): prove fetcher hard-timeout throws + caches error sentinel"
```

---

### Task 10: Green build, README, PR

**Files:**
- Modify: `dotnet/README.md`

- [ ] **Step 1: Update README**

Add to `dotnet/README.md` (after the `WorldMonitor.Data` entry):
```markdown
- `src/WorldMonitor.Caching` — `WorldMonitorCache`, the read-through cache over `ICacheStore`.
  Hand-rolled (not FusionCache) to faithfully reproduce legacy `cachedFetchJson`: in-flight
  coalescing, asymmetric negative-sentinel caching (120s on null / 30s on error + re-throw),
  and an isolate-local outage bridge. All tests are DB-free unit tests using an in-memory store
  fake and a virtual `IClock`.
```

- [ ] **Step 2: Full clean build + test**

Run:
```bash
dotnet build dotnet/WorldMonitor.slnx -c Release
dotnet test dotnet/WorldMonitor.slnx -c Release --filter Category!=Integration
dotnet test dotnet/WorldMonitor.slnx -c Release
```
Expected: build 0/0; the unit ring green (P0 12 + Data 9 + Caching ~13); the full suite green (adds the 10 Data integration tests on LocalDB).

- [ ] **Step 3: Commit, push, open PR**

```bash
git add dotnet/
git commit -m "docs(caching): document WorldMonitor.Caching wrapper"
git push -u origin feat/dotnet-p1a2-cache-wrapper
gh pr create --base feat/dotnet-p1a-cache-substrate --title "P1a-2: WorldMonitorCache read-through wrapper" --body "Implements P1a-2: the read-through cache over ICacheStore reproducing legacy cachedFetchJson — coalescing, asymmetric negative-sentinel (120s null / 30s throw + re-throw), and isolate-local outage bridge. Hand-rolled (NOT FusionCache: the legacy contract re-throws on fetcher error, the opposite of fail-safe). DB-free deterministic unit tests via an in-memory store fake + virtual IClock. Stacked on #2 (P1a-1). Domain entities (P1b) and ML state (P1c) follow."
```
Expected: PR URL printed.

---

## Self-review

**Spec coverage (P1a-2 scope, from the P1 inventory + the verified `cachedFetchJson` reference):**
- ✅ Read-through with tri-state store read; positive hit + `__WM_NEG__` → null — Task 5.
- ✅ In-flight coalescing (single fetch for concurrent misses) — Tasks 5, 6.
- ✅ Asymmetric negative caching: null → 120s sentinel; throw/timeout → 30s sentinel **and re-throw** — Tasks 5, 7, 9 (resolves the critic's #1 fail-safe-vs-sentinel risk by re-throwing, exactly like the legacy code).
- ✅ Isolate-local outage bridge: positive fallback (armed on read-error/write-failure, served on store read error, 30s cap) + negative cooldown short-circuit on store error — Tasks 5, 7, 8.
- ✅ Hard fetcher timeout (default 30s, per-call override, fetcher not cancelled) — Tasks 5, 9.
- ✅ FIFO + expiry + 5000-cap fallback maps, deterministic via `IClock` — Task 3.
- ✅ All constants mirrored from `redis.ts` — Task 2.
- **Deferred (documented in header):** `WithMeta` telemetry, `hasRemoteRedisConfig` distinction, HTTP-tier→TTL mapping/prefixing, DI registration → P2.

**Placeholder scan:** none — every code/command step is complete.

**Type consistency:** `IWorldMonitorCache.GetOrSetAsync<T>` (Task 2) is implemented by `WorldMonitorCache` (Task 5). `BoundedExpiringMap<TValue>` (Task 3) backs both `_negative` (`bool`) and `_positiveFallback` (`object`). `InMemoryCacheStore` (Task 4) implements the P1a-1 `ICacheStore` with the same `CacheReadResult`/`CacheUpsert`/`CachePresence` types. `CacheConstants` names are used throughout Task 5. `TestClock`/`IClock` (`WorldMonitor.Data.Time`) drive both the fake store and the fallback maps.

**Note for execution:** Only the Task 9 timeout test uses real wall-clock (a 50 ms `Task.Delay`); every other time-dependent assertion advances the virtual `TestClock`, so the suite is fast and deterministic. P1a-2 needs no database.
```
