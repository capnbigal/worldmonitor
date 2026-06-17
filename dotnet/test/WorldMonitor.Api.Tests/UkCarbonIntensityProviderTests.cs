using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.UkCarbonIntensityProvider.Feed;

namespace WorldMonitor.Api.Tests;

public class UkCarbonIntensityProviderTests
{
    [Fact]
    public void Feed_binds_carbon_intensity_fields_and_sorts_descending()
    {
        // Shape returned by /generation. Guards the nested "generationmix" / "perc" names.
        const string json = """
        {"data":{"from":"2026-06-17T15:00Z","to":"2026-06-17T15:30Z","generationmix":[
          {"fuel":"biomass","perc":5},
          {"fuel":"coal","perc":0},
          {"fuel":"imports","perc":15.8},
          {"fuel":"gas","perc":15.5},
          {"fuel":"nuclear","perc":8.7},
          {"fuel":"other","perc":2.4},
          {"fuel":"hydro","perc":0.8},
          {"fuel":"solar","perc":18.9},
          {"fuel":"wind","perc":32.9}
        ]}}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var mix = UkCarbonIntensityProvider.MapMix(feed);

        Assert.Equal(9, mix.Count);
        // Sorted by Percent descending.
        Assert.Equal("Wind", mix[0].Fuel);
        Assert.Equal(32.9, mix[0].Percent);
        Assert.Equal("Solar", mix[1].Fuel);
        Assert.Equal(18.9, mix[1].Percent);
        Assert.Equal("Coal", mix[^1].Fuel);
        Assert.Equal(0, mix[^1].Percent);
    }

    [Fact]
    public void MapMix_titlecases_fuel_and_defaults_null_perc()
    {
        const string json = """
        {"data":{"generationmix":[
          {"fuel":"gas","perc":null},
          {"fuel":"wind","perc":10.5}
        ]}}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var mix = UkCarbonIntensityProvider.MapMix(feed);

        Assert.Equal(2, mix.Count);
        Assert.Equal("Wind", mix[0].Fuel);
        Assert.Equal(10.5, mix[0].Percent);
        Assert.Equal("Gas", mix[1].Fuel);
        Assert.Equal(0, mix[1].Percent);   // null perc defaults to 0
    }

    [Fact]
    public void MapMix_skips_rows_without_fuel()
    {
        const string json = """
        {"data":{"generationmix":[
          {"fuel":null,"perc":3},
          {"fuel":"","perc":4},
          {"fuel":"nuclear","perc":7}
        ]}}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var mix = UkCarbonIntensityProvider.MapMix(feed);

        var f = Assert.Single(mix);
        Assert.Equal("Nuclear", f.Fuel);
        Assert.Equal(7, f.Percent);
    }

    [Fact]
    public void MapMix_handles_null_feed_and_null_mix()
    {
        Assert.Empty(UkCarbonIntensityProvider.MapMix(null));
        Assert.Empty(UkCarbonIntensityProvider.MapMix(new Feed(null)));
        Assert.Empty(UkCarbonIntensityProvider.MapMix(
            new Feed(new UkCarbonIntensityProvider.DataDto(null))));
    }
}
