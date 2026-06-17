using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.EonetProvider.EonetFeed;
using Ev = WorldMonitor.Providers.EonetProvider.EonetEvent;
using Cat = WorldMonitor.Providers.EonetProvider.EonetCategory;
using Geom = WorldMonitor.Providers.EonetProvider.EonetGeom;

namespace WorldMonitor.Api.Tests;

public class EonetProviderTests
{
    private static JsonElement Coords(params double[] values) => JsonSerializer.SerializeToElement(values);

    [Fact]
    public void MapEvents_maps_point_geometry_and_latest_geometry()
    {
        var date = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);
        var feed = new Feed(
        [
            new Ev("EONET_1", "Wildfire A", "https://e/1",
                [new Cat("8", "Wildfires")],
                [
                    new Geom(date.AddDays(-1), "Point", Coords(-100.0, 40.0)),   // older
                    new Geom(date, "Point", Coords(-120.5, 38.1)),               // latest — used
                ]),
        ]);

        var events = EonetProvider.MapEvents(feed);

        var e = Assert.Single(events);
        Assert.Equal("EONET_1", e.Id);
        Assert.Equal("Wildfire A", e.Title);
        Assert.Equal("Wildfires", e.Category);
        Assert.Equal("https://e/1", e.Link);
        Assert.Equal(38.1, e.Latitude);       // coords[1] of latest
        Assert.Equal(-120.5, e.Longitude);    // coords[0] of latest
        Assert.Equal(date, e.Date);
    }

    [Fact]
    public void MapEvents_polygon_geometry_yields_no_coordinates()
    {
        var feed = new Feed(
        [
            new Ev("EONET_2", "Storm", null, null,
                [new Geom(null, "Polygon", JsonSerializer.SerializeToElement(new[] { new[] { 1.0, 2.0 } }))]),
        ]);

        var e = Assert.Single(EonetProvider.MapEvents(feed));
        Assert.Null(e.Latitude);
        Assert.Null(e.Longitude);
        Assert.Null(e.Category);    // no categories
    }

    [Fact]
    public void MapEvents_handles_null_and_empty()
    {
        Assert.Empty(EonetProvider.MapEvents(null));
        Assert.Empty(EonetProvider.MapEvents(new Feed(null)));
        Assert.Empty(EonetProvider.MapEvents(new Feed([])));
    }
}
