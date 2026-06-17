using WorldMonitor.Data.Entities.Ml;
using WorldMonitor.Data.Repositories;
using WorldMonitor.Data.Tests.Fakes;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

[Collection("LocalDb")]
[Trait("Category", "Integration")]
public class MlStateTests(LocalDbFixture fx)
{
    private static string K() => "k_" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task Dedup_blocks_within_ttl_and_allows_after_expiry()
    {
        var clock = new TestClock();
        var key = K();
        var repo = new DedupRepository(fx.NewContext(), clock);

        Assert.True(await repo.TryMarkSeenAsync(key, "keyword_spike"));   // first ⇒ newly marked
        Assert.False(await new DedupRepository(fx.NewContext(), clock).TryMarkSeenAsync(key, "keyword_spike")); // within 30m ⇒ duplicate

        clock.Advance(TimeSpan.FromMinutes(31));
        Assert.True(await new DedupRepository(fx.NewContext(), clock).TryMarkSeenAsync(key, "keyword_spike")); // past TTL ⇒ allowed again
    }
}
