using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.FredMacroProvider.ObservationsDto;

namespace WorldMonitor.Api.Tests;

public class FredMacroProviderTests
{
    [Fact]
    public void ParseLatest_reads_the_newest_observation_value()
    {
        const string json = """
        { "count":15000, "limit":1, "sort_order":"desc",
          "observations":[ {"realtime_start":"2026-06-17","realtime_end":"2026-06-17","date":"2026-06-13","value":"4.41"} ] }
        """;
        var dto = JsonSerializer.Deserialize<Dto>(json);

        Assert.Equal(4.41, FredMacroProvider.ParseLatest(dto));
        Assert.Equal("2026-06-13", dto!.Observations![0].Date);
    }

    [Fact]
    public void ParseLatest_returns_null_for_missing_or_dot_values()
    {
        // FRED uses "." for a missing observation.
        var dot = JsonSerializer.Deserialize<Dto>("""{ "observations":[ {"date":"2026-06-13","value":"."} ] }""");
        Assert.Null(FredMacroProvider.ParseLatest(dot));

        Assert.Null(FredMacroProvider.ParseLatest(JsonSerializer.Deserialize<Dto>("""{ "observations":[] }""")));
        Assert.Null(FredMacroProvider.ParseLatest(null));
    }

    [Fact]
    public void Series_are_nonempty_with_ids_names_units()
    {
        Assert.NotEmpty(FredMacroProvider.Series);
        Assert.All(FredMacroProvider.Series, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Id));
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
            Assert.False(string.IsNullOrWhiteSpace(s.Units));
        });
    }
}
