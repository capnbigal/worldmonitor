using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.GdeltIntelProvider.Feed;
using Article = WorldMonitor.Providers.GdeltIntelProvider.Article;

namespace WorldMonitor.Api.Tests;

public class GdeltIntelProviderTests
{
    [Fact]
    public void Feed_binds_gdelt_lowercase_fields()
    {
        // Shape returned by /api/v2/doc/doc?mode=ArtList&format=json. All field names are lowercase
        // compound words (seendate, sourcecountry) that an explicit [JsonPropertyName] must map.
        const string json = """
        {"articles":[
          {"url":"https://zerohedge.com/a","url_mobile":"","title":"Sanctions widen",
           "seendate":"20260617T120000Z","socialimage":"https://img/1.png",
           "domain":"zerohedge.com","language":"English","sourcecountry":"United States"}
        ]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var articles = GdeltIntelProvider.MapArticles(feed);

        var a = Assert.Single(articles);
        Assert.Equal("Sanctions widen", a.Title);
        Assert.Equal("https://zerohedge.com/a", a.Url);
        Assert.Equal("zerohedge.com", a.Domain);
        Assert.Equal("English", a.Language);
        Assert.Equal("United States", a.SourceCountry);
        // 2026-06-17T12:00:00Z == 1781697600000 ms
        Assert.Equal(1781697600000, a.SeenAt);
    }

    [Fact]
    public void MapArticles_keeps_order_and_skips_items_without_title_or_url()
    {
        var feed = new Feed(
        [
            new Article("https://a/1", "First", "20260617T120000Z", "a.com", "English", "United States"),
            new Article("https://a/2", "", "20260617T120000Z", "b.com", "English", "France"),   // no title
            new Article(null, "No url", "20260617T120000Z", "c.com", "English", "Germany"),       // no url
            new Article("https://a/4", "Second", "20260617T130000Z", "d.com", "Spanish", "Spain"),
        ]);

        var articles = GdeltIntelProvider.MapArticles(feed);

        Assert.Equal(2, articles.Count);
        Assert.Equal("First", articles[0].Title);     // API order preserved
        Assert.Equal("Second", articles[1].Title);
    }

    [Fact]
    public void MapArticles_defaults_seenat_to_zero_on_missing_or_unparsable_date()
    {
        var feed = new Feed(
        [
            new Article("https://a/1", "No date", null, "a.com", "English", "United States"),
            new Article("https://a/2", "Bad date", "not-a-date", "b.com", "English", "France"),
        ]);

        var articles = GdeltIntelProvider.MapArticles(feed);

        Assert.Equal(2, articles.Count);
        Assert.Equal(0, articles[0].SeenAt);
        Assert.Equal(0, articles[1].SeenAt);
    }

    [Fact]
    public void MapArticles_handles_null_and_empty()
    {
        Assert.Empty(GdeltIntelProvider.MapArticles(null));
        Assert.Empty(GdeltIntelProvider.MapArticles(new Feed(null)));
        Assert.Empty(GdeltIntelProvider.MapArticles(new Feed([])));
    }
}
