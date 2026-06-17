using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.FearGreedProvider.Feed;
using Reading = WorldMonitor.Providers.FearGreedProvider.Reading;

namespace WorldMonitor.Api.Tests;

public class FearGreedProviderTests
{
    [Fact]
    public void Reading_binds_alternative_me_snake_case_string_fields()
    {
        // Shape returned by /fng/. Guards the gotchas: value & timestamp arrive as JSON STRINGS and the
        // classification field is snake_case (value_classification). Explicit [JsonPropertyName] + string?
        // DTO fields + InvariantCulture parsing in MapReadings fix that.
        const string json = """
        {"name":"Fear and Greed Index",
         "data":[
           {"value":"22","value_classification":"Extreme Fear","timestamp":"1781654400","time_until_update":"41275"},
           {"value":"23","value_classification":"Extreme Fear","timestamp":"1781568000"}],
         "metadata":{"error":null}}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var items = FearGreedProvider.MapReadings(feed);

        Assert.Equal(2, items.Count);
        Assert.Equal(22, items[0].Value);
        Assert.Equal("Extreme Fear", items[0].Classification);
        Assert.Equal(1781654400L * 1000, items[0].At);   // epoch seconds -> milliseconds
        Assert.Equal(23, items[1].Value);
        Assert.Equal(1781568000L * 1000, items[1].At);
    }

    [Fact]
    public void MapReadings_preserves_newest_first_order()
    {
        var feed = new Feed(
        [
            new Reading("60", "Greed", "1781654400"),
            new Reading("40", "Fear", "1781568000"),
        ]);

        var items = FearGreedProvider.MapReadings(feed);

        Assert.Equal(60, items[0].Value);
        Assert.Equal(40, items[1].Value);
    }

    [Fact]
    public void MapReadings_skips_unparseable_value_and_defaults_missing_timestamp()
    {
        var items = FearGreedProvider.MapReadings(new Feed(
        [
            new Reading("abc", "Greed", "1781654400"),   // skipped — bad value
            new Reading("50", null, null),               // kept — null classification/timestamp default
        ]));

        var r = Assert.Single(items);
        Assert.Equal(50, r.Value);
        Assert.Equal("", r.Classification);
        Assert.Equal(0, r.At);
    }

    [Fact]
    public void MapReadings_handles_null_and_empty()
    {
        Assert.Empty(FearGreedProvider.MapReadings(null));
        Assert.Empty(FearGreedProvider.MapReadings(new Feed(null)));
        Assert.Empty(FearGreedProvider.MapReadings(new Feed([])));
    }
}
