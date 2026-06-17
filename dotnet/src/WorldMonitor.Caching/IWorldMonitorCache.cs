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
