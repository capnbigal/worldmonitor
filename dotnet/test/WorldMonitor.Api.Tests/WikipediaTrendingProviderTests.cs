using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class WikipediaTrendingProviderTests
{
    [Fact]
    public void MapArticles_binds_feed_shape_and_prefers_normalized_title()
    {
        // Shape returned by /api/rest_v1/feed/featured/{yyyy}/{MM}/{dd}.
        const string json = """
        {
          "mostread": {
            "date": "2026-06-16Z",
            "articles": [
              {
                "views": 1495154,
                "rank": 1,
                "title": "Oliver_Tree",
                "titles": { "normalized": "Oliver Tree" },
                "description": "American singer",
                "content_urls": { "desktop": { "page": "https://en.wikipedia.org/wiki/Oliver_Tree" } }
              }
            ]
          },
          "tfa": {},
          "news": [],
          "onthisday": []
        }
        """;

        var feed = JsonSerializer.Deserialize<WikipediaTrendingProvider.Feed>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var articles = WikipediaTrendingProvider.MapArticles(feed);

        var a = Assert.Single(articles);
        Assert.Equal("Oliver Tree", a.Title);                                  // normalized, not the underscored title
        Assert.Equal(1495154, a.Views);
        Assert.Equal("American singer", a.Description);
        Assert.Equal("https://en.wikipedia.org/wiki/Oliver_Tree", a.Url);
    }

    [Fact]
    public void MapArticles_keeps_upstream_order_and_falls_back_to_raw_title()
    {
        const string json = """
        {
          "mostread": {
            "articles": [
              { "views": 900, "title": "First_Article" },
              { "views": 800, "title": "Second_Article", "titles": { "normalized": "Second Article" } }
            ]
          }
        }
        """;

        var feed = JsonSerializer.Deserialize<WikipediaTrendingProvider.Feed>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var articles = WikipediaTrendingProvider.MapArticles(feed);

        Assert.Equal(2, articles.Count);
        Assert.Equal("First_Article", articles[0].Title);     // no normalized -> raw title
        Assert.Equal(900, articles[0].Views);
        Assert.Null(articles[0].Description);                 // missing -> null
        Assert.Null(articles[0].Url);                         // missing -> null
        Assert.Equal("Second Article", articles[1].Title);    // order preserved
    }

    [Fact]
    public void MapArticles_skips_entries_without_a_title_and_defaults_views()
    {
        const string json = """
        {
          "mostread": {
            "articles": [
              { "views": 500 },
              { "title": "Has_Title" }
            ]
          }
        }
        """;

        var feed = JsonSerializer.Deserialize<WikipediaTrendingProvider.Feed>(
            json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var articles = WikipediaTrendingProvider.MapArticles(feed);

        var a = Assert.Single(articles);          // the title-less entry is skipped
        Assert.Equal("Has_Title", a.Title);
        Assert.Equal(0, a.Views);                 // missing views -> 0
    }

    [Fact]
    public void MapArticles_returns_empty_when_mostread_or_articles_missing()
    {
        Assert.Empty(WikipediaTrendingProvider.MapArticles(null));
        Assert.Empty(WikipediaTrendingProvider.MapArticles(new WikipediaTrendingProvider.Feed(null)));
        Assert.Empty(WikipediaTrendingProvider.MapArticles(
            new WikipediaTrendingProvider.Feed(new WikipediaTrendingProvider.MostRead(null))));
    }
}
