using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.UsgsVolcanoProvider.VolcanoDto;

namespace WorldMonitor.Api.Tests;

public class UsgsVolcanoProviderTests
{
    [Fact]
    public void VolcanoDto_binds_usgs_snake_case_fields()
    {
        // Shape returned by /hans-public/api/volcano/getElevatedVolcanoes (a JSON array).
        const string json = """
        [{"obs_fullname":"Alaska Volcano Observatory","obs_abbr":"avo","volcano_name":"Great Sitkin",
          "vnum":"311120","notice_type_cd":"DU","sent_utc":"2026-06-16 19:01:27",
          "sent_unixtime":1781636487,"color_code":"ORANGE","alert_level":"WATCH","notice_url":"https://x/n"}]
        """;

        var dtos = JsonSerializer.Deserialize<Dto[]>(json)!;
        var items = UsgsVolcanoProvider.MapVolcanoes(dtos);

        var v = Assert.Single(items);
        Assert.Equal("Great Sitkin", v.Volcano);
        Assert.Equal("ORANGE", v.ColorCode);
        Assert.Equal("WATCH", v.AlertLevel);
        Assert.Equal("Alaska Volcano Observatory", v.Observatory);
        Assert.Equal(1781636487L * 1000, v.At);
        Assert.Equal("https://x/n", v.Url);
    }

    [Fact]
    public void MapVolcanoes_sorts_newest_first_and_skips_nameless()
    {
        var items = UsgsVolcanoProvider.MapVolcanoes(
        [
            new Dto("Old One", "YELLOW", "ADVISORY", "Obs A", 1000, "https://x/old"),
            new Dto("", "RED", "WARNING", "Obs B", 9999, null),       // skipped — no name
            new Dto("New One", "RED", "WARNING", "Obs C", 2000, null),
        ]);

        Assert.Equal(2, items.Count);
        Assert.Equal("New One", items[0].Volcano);                    // 2000 newest
        Assert.Equal(2000L * 1000, items[0].At);
        Assert.Equal("Old One", items[1].Volcano);
    }

    [Fact]
    public void MapVolcanoes_defaults_nulls()
    {
        var items = UsgsVolcanoProvider.MapVolcanoes(
        [
            new Dto("Kilauea", null, null, null, null, null),
        ]);

        var v = Assert.Single(items);
        Assert.Equal("Kilauea", v.Volcano);
        Assert.Equal("UNKNOWN", v.ColorCode);
        Assert.Equal("", v.AlertLevel);
        Assert.Null(v.Observatory);
        Assert.Equal(0, v.At);
        Assert.Null(v.Url);
    }

    [Fact]
    public void MapVolcanoes_handles_null()
    {
        Assert.Empty(UsgsVolcanoProvider.MapVolcanoes(null));
    }
}
