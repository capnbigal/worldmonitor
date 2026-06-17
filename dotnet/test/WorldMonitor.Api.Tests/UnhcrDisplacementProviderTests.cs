using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.UnhcrDisplacementProvider.Feed;

namespace WorldMonitor.Api.Tests;

public class UnhcrDisplacementProviderTests
{
    [Fact]
    public void MapCountries_reads_string_and_number_counts_and_sorts_descending()
    {
        // UNHCR returns refugees/asylum_seekers/idps as a JSON number (5766586) OR a JSON string ("0").
        // Afghanistan uses numbers; Anguilla mixes a string idps/refugees with a numeric asylum_seekers.
        const string json = """
        {"page":1,"maxPages":69,"items":[
          {"year":2024,"coo_name":"Afghanistan","coo":"AFG","coa_name":"-",
           "refugees":5766586,"asylum_seekers":384732,"idps":3199710},
          {"year":2024,"coo_name":"Anguilla","coo":"AIA","coa_name":"-",
           "refugees":"0","asylum_seekers":5,"idps":"0"}
        ]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var items = UnhcrDisplacementProvider.MapCountries(feed);

        Assert.Equal(2, items.Count);

        // Sorted by total displaced descending — Afghanistan first.
        Assert.Equal("Afghanistan", items[0].Country);
        Assert.Equal(5766586, items[0].Refugees);       // JSON number
        Assert.Equal(384732, items[0].AsylumSeekers);
        Assert.Equal(3199710, items[0].Idps);

        Assert.Equal("Anguilla", items[1].Country);
        Assert.Equal(0, items[1].Refugees);             // JSON string "0"
        Assert.Equal(5, items[1].AsylumSeekers);        // JSON number
        Assert.Equal(0, items[1].Idps);                 // JSON string "0"
    }

    [Fact]
    public void MapCountries_skips_placeholder_and_unknown_origins()
    {
        const string json = """
        {"items":[
          {"coo_name":"-","refugees":1,"asylum_seekers":1,"idps":1},
          {"coo_name":"Unknown","refugees":2,"asylum_seekers":2,"idps":2},
          {"coo_name":"","refugees":3,"asylum_seekers":3,"idps":3},
          {"coo_name":"Syrian Arab Rep.","refugees":"6000000","asylum_seekers":"100000","idps":"7000000"}
        ]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var items = UnhcrDisplacementProvider.MapCountries(feed);

        var only = Assert.Single(items);
        Assert.Equal("Syrian Arab Rep.", only.Country);
        Assert.Equal(6000000, only.Refugees);   // string-typed count parsed
        Assert.Equal(100000, only.AsylumSeekers);
        Assert.Equal(7000000, only.Idps);
    }

    [Fact]
    public void MapCountries_handles_null_and_empty()
    {
        Assert.Empty(UnhcrDisplacementProvider.MapCountries(null));
        Assert.Empty(UnhcrDisplacementProvider.MapCountries(new Feed(null)));
        Assert.Empty(UnhcrDisplacementProvider.MapCountries(new Feed([])));
    }
}
