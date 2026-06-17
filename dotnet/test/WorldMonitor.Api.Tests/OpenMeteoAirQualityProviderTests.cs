using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Forecast = WorldMonitor.Providers.OpenMeteoAirQualityProvider.Forecast;
using Current = WorldMonitor.Providers.OpenMeteoAirQualityProvider.Current;

namespace WorldMonitor.Api.Tests;

public class OpenMeteoAirQualityProviderTests
{
    private static readonly IReadOnlyList<(string City, double Lat, double Lon)> SampleCities =
    [
        ("London", 51.51, -0.13),
        ("New York", 40.71, -74.01),
    ];

    [Fact]
    public void Forecast_binds_open_meteo_snake_case_fields()
    {
        // Shape returned by /v1/air-quality with comma-separated coords: a JSON array, one object per coord.
        const string json = """
        [{"latitude":51.5,"longitude":-0.1,"current":{"time":"2026-06-17T12:00","interval":3600,"european_aqi":20,"pm2_5":4.2,"pm10":6.5}},
         {"latitude":40.71,"longitude":-74.01,"current":{"time":"2026-06-17T12:00","interval":3600,"european_aqi":55,"pm2_5":12.3,"pm10":18.9}}]
        """;

        var rows = JsonSerializer.Deserialize<Forecast[]>(json)!;
        var items = OpenMeteoAirQualityProvider.MapForecasts(rows, SampleCities);

        Assert.Equal(2, items.Count);

        var london = items[0];
        Assert.Equal("London", london.City);
        Assert.Equal(20, london.Aqi);
        Assert.Equal(4.2, london.Pm25);
        Assert.Equal(6.5, london.Pm10);

        var ny = items[1];
        Assert.Equal("New York", ny.City);
        Assert.Equal(55, ny.Aqi);
        Assert.Equal(12.3, ny.Pm25);
        Assert.Equal(18.9, ny.Pm10);
    }

    [Fact]
    public void MapForecasts_zips_rows_to_cities_by_index()
    {
        var rows = new[]
        {
            new Forecast(new Current(15, 3.0, 5.0)),
            new Forecast(new Current(70, 25.0, 40.0)),
        };

        var items = OpenMeteoAirQualityProvider.MapForecasts(rows, SampleCities);

        Assert.Equal("London", items[0].City);
        Assert.Equal(15, items[0].Aqi);
        Assert.Equal("New York", items[1].City);
        Assert.Equal(70, items[1].Aqi);
    }

    [Fact]
    public void MapForecasts_defaults_missing_current_and_missing_rows()
    {
        var rows = new[]
        {
            new Forecast(null),   // missing current block
        };

        var items = OpenMeteoAirQualityProvider.MapForecasts(rows, SampleCities);

        Assert.Equal(2, items.Count);
        // first city: present row but null current => zeros
        Assert.Equal("London", items[0].City);
        Assert.Equal(0, items[0].Aqi);
        Assert.Equal(0, items[0].Pm25);
        Assert.Equal(0, items[0].Pm10);
        // second city: no row at all => zeros
        Assert.Equal("New York", items[1].City);
        Assert.Equal(0, items[1].Aqi);
    }

    [Fact]
    public void MapForecasts_handles_null_rows()
    {
        var items = OpenMeteoAirQualityProvider.MapForecasts(null, SampleCities);

        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(0, i.Aqi));
    }
}
