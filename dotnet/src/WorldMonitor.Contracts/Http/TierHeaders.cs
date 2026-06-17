using System.Collections.Frozen;

namespace WorldMonitor.Contracts.Http;

/// <summary>Cache-Control + CDN-Cache-Control strings per tier, mirrored from the legacy gateway.</summary>
public static class TierHeaders
{
    public static readonly FrozenDictionary<CacheTier, string> CacheControl = new Dictionary<CacheTier, string>
    {
        [CacheTier.Fast]        = "public, max-age=60, s-maxage=300, stale-while-revalidate=60, stale-if-error=600",
        [CacheTier.Medium]      = "public, max-age=120, s-maxage=600, stale-while-revalidate=120, stale-if-error=900",
        [CacheTier.Slow]        = "public, max-age=300, s-maxage=1800, stale-while-revalidate=300, stale-if-error=3600",
        [CacheTier.SlowBrowser] = "max-age=300, stale-while-revalidate=60, stale-if-error=1800",
        [CacheTier.Static]      = "public, max-age=600, s-maxage=3600, stale-while-revalidate=600, stale-if-error=14400",
        [CacheTier.Daily]       = "public, max-age=3600, s-maxage=14400, stale-while-revalidate=7200, stale-if-error=172800",
        [CacheTier.NoStore]     = "no-store",
        [CacheTier.Live]        = "public, max-age=30, s-maxage=60, stale-while-revalidate=60, stale-if-error=300",
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<CacheTier, string?> CdnCacheControl = new Dictionary<CacheTier, string?>
    {
        [CacheTier.Fast]        = "public, s-maxage=600, stale-while-revalidate=300, stale-if-error=1200",
        [CacheTier.Medium]      = "public, s-maxage=1200, stale-while-revalidate=600, stale-if-error=1800",
        [CacheTier.Slow]        = "public, s-maxage=3600, stale-while-revalidate=900, stale-if-error=7200",
        [CacheTier.SlowBrowser] = "public, s-maxage=900, stale-while-revalidate=60, stale-if-error=1800",
        [CacheTier.Static]      = "public, s-maxage=14400, stale-while-revalidate=3600, stale-if-error=28800",
        [CacheTier.Daily]       = "public, s-maxage=86400, stale-while-revalidate=14400, stale-if-error=172800",
        [CacheTier.NoStore]     = null,
        [CacheTier.Live]        = "public, s-maxage=60, stale-while-revalidate=60, stale-if-error=300",
    }.ToFrozenDictionary();
}
