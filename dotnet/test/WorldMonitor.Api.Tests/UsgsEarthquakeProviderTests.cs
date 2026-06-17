using WorldMonitor.Providers;
using Xunit;
using Geom = WorldMonitor.Providers.UsgsEarthquakeProvider.UsgsGeom;
using Feature = WorldMonitor.Providers.UsgsEarthquakeProvider.UsgsFeature;
using Feed = WorldMonitor.Providers.UsgsEarthquakeProvider.UsgsFeed;
using Props = WorldMonitor.Providers.UsgsEarthquakeProvider.UsgsProps;

namespace WorldMonitor.Api.Tests;

public class UsgsEarthquakeProviderTests
{
    [Fact]
    public void MapFeed_maps_geojson_to_earthquake_dtos()
    {
        var feed = new Feed(
        [
            new Feature("us1", new Props("10 km N of X", 5.2, 1_700_000_000_000, "https://u/us1"),
                new Geom([-120.5, 38.1, 12.3])),       // [lon, lat, depthKm]
            new Feature("bad", Properties: null, Geometry: null),    // skipped — incomplete
            new Feature("short", new Props(null, null, 1, null), new Geom([1.0, 2.0])), // skipped — coords < 3
        ]);

        var quakes = UsgsEarthquakeProvider.MapFeed(feed);

        var q = Assert.Single(quakes);
        Assert.Equal("us1", q.Id);
        Assert.Equal("10 km N of X", q.Place);
        Assert.Equal(5.2, q.Magnitude);
        Assert.Equal(12.3, q.DepthKm);
        Assert.Equal(38.1, q.Location!.Latitude);     // coords[1]
        Assert.Equal(-120.5, q.Location.Longitude);    // coords[0]
        Assert.Equal(1_700_000_000_000, q.OccurredAt);
        Assert.Equal("https://u/us1", q.SourceUrl);
    }

    [Fact]
    public void MapFeed_null_magnitude_defaults_to_zero()
    {
        var feed = new Feed([new Feature("u", new Props("p", Mag: null, 0, null), new Geom([0, 0, 0]))]);
        Assert.Equal(0, UsgsEarthquakeProvider.MapFeed(feed)[0].Magnitude);
    }

    [Fact]
    public void MapFeed_handles_null_and_empty_feeds()
    {
        Assert.Empty(UsgsEarthquakeProvider.MapFeed(null));
        Assert.Empty(UsgsEarthquakeProvider.MapFeed(new Feed(null)));
        Assert.Empty(UsgsEarthquakeProvider.MapFeed(new Feed([])));
    }
}
