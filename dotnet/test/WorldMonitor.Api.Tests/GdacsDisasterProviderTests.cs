using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class GdacsDisasterProviderTests
{
    private const string SampleRss = """
    <?xml version="1.0" encoding="UTF-8"?>
    <rss version="2.0">
      <channel>
        <title>GDACS RSS information</title>
        <item>
          <title>Green flood alert in Türkiye</title>
          <link>https://www.gdacs.org/report.aspx?eventid=1</link>
          <description>Flood alert</description>
          <pubDate>Tue, 16 Jun 2026 10:00:00 GMT</pubDate>
        </item>
        <item>
          <title>Red earthquake alert in Chile</title>
          <link>https://www.gdacs.org/report.aspx?eventid=2</link>
          <description>Earthquake alert</description>
          <pubDate>Tue, 16 Jun 2026 09:00:00 GMT</pubDate>
        </item>
        <item>
          <title></title>
          <link>https://www.gdacs.org/report.aspx?eventid=3</link>
        </item>
      </channel>
    </rss>
    """;

    [Fact]
    public void MapFeed_extracts_alert_level_title_and_link_and_skips_titleless()
    {
        var items = GdacsDisasterProvider.MapFeed(SampleRss);

        Assert.Equal(2, items.Count);                  // the empty-title item is skipped

        var green = items[0];
        Assert.Equal("Green flood alert in Türkiye", green.Title);
        Assert.Equal("Green", green.AlertLevel);
        Assert.Equal("https://www.gdacs.org/report.aspx?eventid=1", green.Link);
        Assert.True(green.At > 0);

        var red = items[1];
        Assert.Equal("Red earthquake alert in Chile", red.Title);
        Assert.Equal("Red", red.AlertLevel);
        Assert.Equal("https://www.gdacs.org/report.aspx?eventid=2", red.Link);
    }

    [Fact]
    public void MapFeed_returns_unknown_for_non_color_titles()
    {
        const string rss = """
        <?xml version="1.0" encoding="UTF-8"?>
        <rss version="2.0">
          <channel>
            <title>GDACS</title>
            <item>
              <title>Tropical cyclone advisory</title>
              <link>https://www.gdacs.org/x</link>
            </item>
          </channel>
        </rss>
        """;

        var item = Assert.Single(GdacsDisasterProvider.MapFeed(rss));
        Assert.Equal("Unknown", item.AlertLevel);
        Assert.Equal("Tropical cyclone advisory", item.Title);
        Assert.Equal(0, item.At);                       // no pubDate -> 0
    }
}
