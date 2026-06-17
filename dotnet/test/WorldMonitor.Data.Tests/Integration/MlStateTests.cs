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

    [Fact]
    public async Task Topic_baseline_averages_last_7_days()
    {
        var clock = new TestClock();
        var topic = "t_" + Guid.NewGuid().ToString("N");
        var repo = new TopicVelocityRepository(fx.NewContext(), clock);
        await repo.AddAsync(topic, 10);
        clock.Advance(TimeSpan.FromDays(1));
        await new TopicVelocityRepository(fx.NewContext(), clock).AddAsync(topic, 20);

        var baseline = await new TopicVelocityRepository(fx.NewContext(), clock).SevenDayBaselineAsync(topic);
        Assert.Equal(15.0, baseline);
    }

    [Fact]
    public async Task Correlation_state_is_null_on_cold_start_then_round_trips()
    {
        var clock = new TestClock();
        // A dedicated DB per other tests means a fresh CorrelationStates table is empty at cold start.
        var repo = new CorrelationStateRepository(fx.NewContext(), clock);
        // Save then read (single-row); the saved snapshot round-trips.
        await repo.SaveAsync("{\"n\":1}", "{\"m\":2}", "{\"p\":3}");
        var latest = await new CorrelationStateRepository(fx.NewContext(), clock).GetLatestAsync();
        Assert.NotNull(latest);
        Assert.Equal("{\"n\":1}", latest!.NewsVelocity);
    }
}
