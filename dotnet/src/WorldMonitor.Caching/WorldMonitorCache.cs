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
