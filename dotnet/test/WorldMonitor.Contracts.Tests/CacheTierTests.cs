using WorldMonitor.Contracts.Http;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class CacheTierTests
{
    [Fact]
    public void Fast_tier_cache_control_matches_legacy_gateway()
    {
        Assert.Equal(
            "public, max-age=60, s-maxage=300, stale-while-revalidate=60, stale-if-error=600",
            TierHeaders.CacheControl[CacheTier.Fast]);
    }

    [Fact]
    public void Daily_tier_cdn_cache_control_matches_legacy_gateway()
    {
        Assert.Equal(
            "public, s-maxage=86400, stale-while-revalidate=14400, stale-if-error=172800",
            TierHeaders.CdnCacheControl[CacheTier.Daily]);
    }

    [Fact]
    public void NoStore_tier_matches_legacy_gateway()
    {
        Assert.Equal("no-store", TierHeaders.CacheControl[CacheTier.NoStore]);
        Assert.Null(TierHeaders.CdnCacheControl[CacheTier.NoStore]);
    }

    [Fact]
    public void Every_tier_has_both_headers()
    {
        foreach (var tier in Enum.GetValues<CacheTier>())
        {
            Assert.True(TierHeaders.CacheControl.ContainsKey(tier), $"missing Cache-Control for {tier}");
            Assert.True(TierHeaders.CdnCacheControl.ContainsKey(tier), $"missing CDN-Cache-Control for {tier}");
        }
    }
}
