using WorldMonitor.Data.Repositories;
using Xunit;

namespace WorldMonitor.Data.Tests.Unit;

public class DedupTtlTests
{
    [Theory]
    [InlineData("silent_divergence", 6 * 60)]
    [InlineData("flow_price_divergence", 6 * 60)]
    [InlineData("explained_market_move", 6 * 60)]
    [InlineData("prediction_leads_news", 2 * 60)]
    [InlineData("keyword_spike", 30)]
    [InlineData("anything_else", 30)]   // default
    public void TtlFor_matches_legacy_minutes(string signalType, int expectedMinutes)
        => Assert.Equal(expectedMinutes, (int)DedupRepository.TtlFor(signalType).TotalMinutes);
}
