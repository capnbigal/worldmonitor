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
