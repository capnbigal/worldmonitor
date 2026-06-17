using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.FinnhubNewsProvider.NewsDto;

namespace WorldMonitor.Api.Tests;

public class FinnhubNewsProviderTests
{
    [Fact]
    public void MapNews_maps_fields_skips_empties_and_dedupes_by_link()
    {
        var items = new[]
        {
            new Dto(7460000, "Markets rally on rate cut hopes", "Stocks climbed…", "https://example.com/a", "Reuters", 1781700000),
            new Dto(7460001, "", "no headline row should be skipped", "https://example.com/b", "AP", 1781700100),
            new Dto(7460002, "Duplicate link is deduped", "second copy", "https://example.com/a", "Bloomberg", 1781700200),
            new Dto(7460003, "Fed minutes released", null, "https://example.com/c", null, 1781700300),
            new Dto(null, "Missing id falls back to url", "summary", "https://example.com/d", "CNBC", 1781700400),
        };

        var mapped = FinnhubNewsProvider.MapNews(items);

        // 5 inputs: one empty headline skipped, one duplicate url deduped → 3 remain, in order.
        Assert.Equal(3, mapped.Count);

        Assert.Equal("7460000", mapped[0].Id);
        Assert.Equal("Markets rally on rate cut hopes", mapped[0].Title);
        Assert.Equal("Stocks climbed…", mapped[0].Summary);
        Assert.Equal("https://example.com/a", mapped[0].Link);
        Assert.Equal("Reuters", mapped[0].Source);
        Assert.Equal(1781700000L * 1000, mapped[0].PublishedAt);

        Assert.Equal("Fed minutes released", mapped[1].Title);
        Assert.Null(mapped[1].Summary);
        Assert.Equal("", mapped[1].Source);

        // Missing id falls back to the url.
        Assert.Equal("https://example.com/d", mapped[2].Id);
    }

    [Fact]
    public void MapNews_handles_null_and_empty()
    {
        Assert.Empty(FinnhubNewsProvider.MapNews(null));
        Assert.Empty(FinnhubNewsProvider.MapNews([]));
    }
}
