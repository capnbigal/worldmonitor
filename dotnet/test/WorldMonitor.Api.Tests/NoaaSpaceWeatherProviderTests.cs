using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.NoaaSpaceWeatherProvider.KpDto;

namespace WorldMonitor.Api.Tests;

public class NoaaSpaceWeatherProviderTests
{
    [Fact]
    public void KpDto_binds_noaa_fields_including_capital_kp()
    {
        // Shape returned by products/noaa-planetary-k-index.json: an array of objects, oldest first.
        // Guards the casing gotcha: JsonNamingPolicy.SnakeCaseLower would map Kp -> "kp" and the
        // snake_case "time_tag" -> "timeTag", both binding null. Explicit [JsonPropertyName] fixes that.
        const string json = """
        [{"time_tag":"2026-06-10T00:00:00","Kp":2.00,"a_running":7,"station_count":8},
         {"time_tag":"2026-06-10T03:00:00","Kp":1.33,"a_running":5,"station_count":8}]
        """;

        var dtos = JsonSerializer.Deserialize<Dto[]>(json)!;
        var readings = NoaaSpaceWeatherProvider.MapReadings(dtos, 24);

        // Newest-first: the 03:00 reading comes first.
        Assert.Equal(2, readings.Count);
        Assert.Equal(1.33, readings[0].Kp);
        Assert.Equal(2.00, readings[1].Kp);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 10, 3, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            readings[0].At);
        Assert.Equal(
            new DateTimeOffset(2026, 6, 10, 0, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            readings[1].At);
    }

    [Fact]
    public void MapReadings_treats_time_tag_as_utc()
    {
        var readings = NoaaSpaceWeatherProvider.MapReadings(
        [
            new Dto("2026-06-10T12:00:00", 3.0),
        ]);

        var r = Assert.Single(readings);
        // 12:00 with no zone suffix must be interpreted as UTC, not local.
        Assert.Equal(
            new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds(),
            r.At);
    }

    [Fact]
    public void MapReadings_returns_most_recent_count_newest_first()
    {
        // Oldest-first input of 5 readings; ask for the most recent 3.
        var readings = NoaaSpaceWeatherProvider.MapReadings(
        [
            new Dto("2026-06-10T00:00:00", 1.0),
            new Dto("2026-06-10T03:00:00", 2.0),
            new Dto("2026-06-10T06:00:00", 3.0),
            new Dto("2026-06-10T09:00:00", 4.0),
            new Dto("2026-06-10T12:00:00", 5.0),
        ], 3);

        Assert.Equal(3, readings.Count);
        Assert.Equal(5.0, readings[0].Kp);   // newest
        Assert.Equal(4.0, readings[1].Kp);
        Assert.Equal(3.0, readings[2].Kp);
    }

    [Fact]
    public void MapReadings_defaults_null_kp_and_skips_null_time_tag()
    {
        var readings = NoaaSpaceWeatherProvider.MapReadings(
        [
            new Dto(null, 4.0),                    // skipped — no time_tag
            new Dto("2026-06-10T00:00:00", null),  // Kp defaults to 0
        ]);

        var r = Assert.Single(readings);
        Assert.Equal(0, r.Kp);
    }

    [Fact]
    public void MapReadings_skips_unparseable_time_tag()
    {
        var readings = NoaaSpaceWeatherProvider.MapReadings(
        [
            new Dto("not-a-date", 4.0),
            new Dto("2026-06-10T00:00:00", 2.0),
        ]);

        var r = Assert.Single(readings);
        Assert.Equal(2.0, r.Kp);
    }

    [Fact]
    public void MapReadings_handles_null()
    {
        Assert.Empty(NoaaSpaceWeatherProvider.MapReadings(null));
    }
}
