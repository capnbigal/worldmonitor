using WorldMonitor.Contracts.News;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class RssNewsProviderTests
{
    private const string SampleRss = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0">
      <channel>
        <title>Example World</title>
        <item>
          <title>Headline One</title>
          <link>https://example.com/a</link>
          <description>&lt;p&gt;Some &lt;b&gt;summary&lt;/b&gt; text.&lt;/p&gt;</description>
          <pubDate>Tue, 16 Jun 2026 10:00:00 GMT</pubDate>
          <guid>guid-a</guid>
        </item>
        <item>
          <title>Headline Two</title>
          <link>https://example.com/b</link>
          <pubDate>Tue, 16 Jun 2026 09:00:00 GMT</pubDate>
        </item>
        <item>
          <title></title>
          <link>https://example.com/c</link>
        </item>
      </channel>
    </rss>
    """;

    [Fact]
    public void MapFeed_parses_items_strips_html_and_skips_titleless()
    {
        var items = RssNewsProvider.MapFeed("Example", SampleRss);

        Assert.Equal(2, items.Count);                          // the empty-title item is skipped
        var first = items[0];
        Assert.Equal("Headline One", first.Title);
        Assert.Equal("https://example.com/a", first.Link);
        Assert.Equal("Example", first.Source);
        Assert.Equal("Some summary text.", first.Summary);     // tags stripped, entities decoded
        Assert.True(first.PublishedAt > 0);
        Assert.Equal("guid-a", first.Id);
    }

    [Fact]
    public void Merge_dedupes_by_link_and_title_sorts_newest_first_and_caps()
    {
        NewsItem Item(string id, string title, string link, long at) =>
            new() { Id = id, Title = title, Link = link, Source = "S", PublishedAt = at };

        var merged = RssNewsProvider.Merge(
        [
            Item("1", "Alpha", "https://x/1", 100),   // older twin of #4 by link -> dropped
            Item("2", "Beta", "https://x/2", 300),
            Item("3", "Beta", "https://x/3", 250),    // duplicate title -> dropped
            Item("4", "Alpha", "https://x/1", 400),   // newest; survives the link-dedup
            Item("5", "Gamma", "https://x/5", 200),   // pushed out by the count cap
        ], count: 2);

        Assert.Equal(2, merged.Count);                 // capped at 2
        Assert.Equal("Alpha", merged[0].Title);        // 400 newest
        Assert.Equal("4", merged[0].Id);               // kept the newest of the duplicate-link pair
        Assert.Equal("Beta", merged[1].Title);         // 300 next
    }

    [Fact]
    public void Feeds_are_all_https_and_nonempty()
    {
        Assert.NotEmpty(RssNewsProvider.Feeds);
        Assert.All(RssNewsProvider.Feeds, f =>
        {
            Assert.False(string.IsNullOrWhiteSpace(f.Source));
            Assert.StartsWith("https://", f.Url);
        });
    }
}
