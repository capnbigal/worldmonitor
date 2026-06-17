using System.Text.Json;
using WorldMonitor.Contracts.Json;
using WorldMonitor.Contracts.Seismology;
using Xunit;

namespace WorldMonitor.Contracts.Tests;

public class SeismologyWireParityTests
{
    private static string FixtureJson() =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "seismology-list-earthquakes.json"));

    [Fact]
    public void Deserializes_golden_fixture()
    {
        var resp = JsonSerializer.Deserialize<ListEarthquakesResponse>(FixtureJson(), WmJson.Options)!;

        var eq = Assert.Single(resp.Earthquakes);
        Assert.Equal("us7000abcd", eq.Id);
        Assert.Equal(34.1, eq.DepthKm);
        Assert.Equal(1718530000000L, eq.OccurredAt);
        Assert.False(eq.NearTestSite);
        Assert.Null(eq.TestSiteName);
        Assert.Equal(1, resp.Pagination!.TotalCount);
    }

    [Fact]
    public void Serializes_with_camelCase_number_int64_and_omitted_nulls()
    {
        var eq = new Earthquake
        {
            Id = "us7000abcd",
            Place = "10 km SW of Anchorage, Alaska",
            Magnitude = 5.2,
            DepthKm = 34.1,
            OccurredAt = 1718530000000L,
            SourceUrl = "https://example/us7000abcd",
            NearTestSite = false,
            ConcernScore = 42.5,
            ConcernLevel = "moderate",
        };

        var json = JsonSerializer.Serialize(eq, WmJson.Options);

        Assert.Contains("\"depthKm\":34.1", json);
        Assert.Contains("\"occurredAt\":1718530000000", json);   // number, no quotes
        Assert.Contains("\"nearTestSite\":false", json);
        Assert.DoesNotContain("testSiteName", json);             // null omitted
        Assert.DoesNotContain("depth_km", json);                 // not snake_case
    }

    [Fact]
    public void Query_param_name_map_is_snake_case()
    {
        Assert.Equal("page_size", ListEarthquakesRequest.QueryNames[nameof(ListEarthquakesRequest.PageSize)]);
        Assert.Equal("min_magnitude", ListEarthquakesRequest.QueryNames[nameof(ListEarthquakesRequest.MinMagnitude)]);
    }
}
