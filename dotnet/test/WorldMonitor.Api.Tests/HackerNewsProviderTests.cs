using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.HackerNewsProvider.ItemDto;

namespace WorldMonitor.Api.Tests;

public class HackerNewsProviderTests
{
    [Fact]
    public void MapStory_binds_item_json_fields()
    {
        // Shape returned by /v0/item/{id}.json for a normal link story.
        const string json = """
        {"id":42,"title":"Show HN: a thing","by":"pg","score":123,"time":1700000000,
         "url":"https://example.com/a","descendants":45,"type":"story"}
        """;

        var dto = JsonSerializer.Deserialize<Dto>(json)!;
        var story = HackerNewsProvider.MapStory(dto);

        Assert.NotNull(story);
        Assert.Equal(42, story!.Id);
        Assert.Equal("Show HN: a thing", story.Title);
        Assert.Equal("https://example.com/a", story.Url);
        Assert.Equal(123, story.Score);
        Assert.Equal("pg", story.By);
        Assert.Equal(45, story.Comments);
        Assert.Equal(1700000000L * 1000, story.At);   // epoch seconds → milliseconds
    }

    [Fact]
    public void MapStory_handles_text_post_without_url_and_missing_fields()
    {
        // Ask HN text post: no 'url', no 'descendants', no 'score'.
        const string json = """
        {"id":7,"title":"Ask HN: what are you working on?","by":"alice","time":1700000500,"type":"story"}
        """;

        var dto = JsonSerializer.Deserialize<Dto>(json)!;
        var story = HackerNewsProvider.MapStory(dto);

        Assert.NotNull(story);
        Assert.Equal(7, story!.Id);
        Assert.Equal("Ask HN: what are you working on?", story.Title);
        Assert.Null(story.Url);            // text post has no url
        Assert.Equal(0, story.Score);      // missing → 0
        Assert.Equal("alice", story.By);
        Assert.Equal(0, story.Comments);   // missing descendants → 0
        Assert.Equal(1700000500L * 1000, story.At);
    }

    [Fact]
    public void MapStory_returns_null_for_null_or_titleless_item()
    {
        Assert.Null(HackerNewsProvider.MapStory(null));

        var titleless = JsonSerializer.Deserialize<Dto>("""{"id":1,"by":"bob","time":1700000000}""")!;
        Assert.Null(HackerNewsProvider.MapStory(titleless));
    }
}
