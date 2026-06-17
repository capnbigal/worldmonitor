using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class TechNewsProviderTests
{
    [Fact]
    public void Feeds_are_all_https_and_nonempty()
    {
        Assert.NotEmpty(TechNewsProvider.Feeds);
        Assert.All(TechNewsProvider.Feeds, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
            Assert.StartsWith("https://", f.Url);
        });
    }

    [Fact]
    public void Feeds_are_distinct_from_world_news_feeds()
    {
        // The tech panel reuses RssNewsProvider's parsing/merge but must carry its own (different) feed set.
        var techUrls = TechNewsProvider.Feeds.Select(f => f.Url).ToHashSet();
        var worldUrls = RssNewsProvider.Feeds.Select(f => f.Url).ToHashSet();
        Assert.Empty(techUrls.Intersect(worldUrls));
    }
}
