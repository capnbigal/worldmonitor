using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class NwsAlertProviderTests
{
    // Shape returned by api.weather.gov/alerts/active. 'sent' is ISO-8601 with a UTC offset.
    private const string SampleJson = """
    {"features":[
      {"properties":{"event":"Flood Warning","severity":"Severe","headline":"Flood Warning issued June 17",
        "areaDesc":"East Baton Rouge, LA","sent":"2026-06-17T10:53:00-05:00"}},
      {"properties":{"event":"Tornado Warning","severity":"Extreme","headline":"Tornado Warning issued June 17",
        "areaDesc":"Cleveland, OK","sent":"2026-06-17T12:00:00-05:00"}}
    ]}
    """;

    [Fact]
    public void MapAlerts_binds_fields_parses_sent_and_sorts_newest_first()
    {
        var feed = JsonSerializer.Deserialize<NwsAlertProvider.Feed>(SampleJson)!;
        var alerts = NwsAlertProvider.MapAlerts(feed);

        Assert.Equal(2, alerts.Count);

        // Tornado was sent at 12:00-05:00 (newer) than Flood at 10:53-05:00 -> sorts first.
        var first = alerts[0];
        Assert.Equal("Tornado Warning", first.Event);
        Assert.Equal("Extreme", first.Severity);
        Assert.Equal("Cleveland, OK", first.Area);
        Assert.Equal("Tornado Warning issued June 17", first.Headline);
        Assert.Equal(DateTimeOffset.Parse("2026-06-17T12:00:00-05:00").ToUnixTimeMilliseconds(), first.At);

        var second = alerts[1];
        Assert.Equal("Flood Warning", second.Event);
        Assert.Equal("Severe", second.Severity);
        Assert.True(first.At > second.At);
    }

    [Fact]
    public void MapAlerts_defaults_severity_and_skips_eventless_features()
    {
        const string json = """
        {"features":[
          {"properties":{"event":null,"severity":"Severe","areaDesc":"X","sent":"2026-06-17T10:00:00-05:00"}},
          {"properties":{"event":"Special Weather Statement","severity":null,"areaDesc":null,"headline":null,"sent":"bad-date"}}
        ]}
        """;

        var feed = JsonSerializer.Deserialize<NwsAlertProvider.Feed>(json)!;
        var alerts = NwsAlertProvider.MapAlerts(feed);

        var a = Assert.Single(alerts);                 // eventless feature skipped
        Assert.Equal("Special Weather Statement", a.Event);
        Assert.Equal("", a.Severity);                  // null severity defaults to ""
        Assert.Null(a.Area);
        Assert.Null(a.Headline);
        Assert.Equal(0, a.At);                          // unparseable 'sent' -> 0
    }

    [Fact]
    public void MapAlerts_handles_null_and_missing_features()
    {
        Assert.Empty(NwsAlertProvider.MapAlerts(null));
        Assert.Empty(NwsAlertProvider.MapAlerts(new NwsAlertProvider.Feed(null)));
    }
}
