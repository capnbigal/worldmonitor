using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Forecast = WorldMonitor.Providers.OpenMeteoWeatherProvider.Forecast;
using Current = WorldMonitor.Providers.OpenMeteoWeatherProvider.Current;

namespace WorldMonitor.Api.Tests;

public class OpenMeteoWeatherProviderTests
{
    private static readonly IReadOnlyList<(string City, double Lat, double Lon)> Cities =
    [
        ("London", 51.51, -0.13),
        ("New York", 40.71, -74.01),
        ("Tokyo", 35.68, 139.69),
    ];

    [Fact]
    public void Forecast_binds_openmeteo_snake_case_fields()
    {
        // Shape returned by /v1/forecast with multiple CSV coords: a JSON ARRAY, one object per coord.
        // Guards the snake_case binding (temperature_2m / weather_code / wind_speed_10m).
        const string json = """
        [{"latitude":51.5,"longitude":-0.13,
          "current":{"time":"2026-06-17T12:45","interval":900,"temperature_2m":23.9,"weather_code":2,"wind_speed_10m":18.7}}]
        """;

        var rows = JsonSerializer.Deserialize<Forecast[]>(json)!;
        var current = Assert.Single(rows).Current!;
        Assert.Equal(23.9, current.Temperature2m);
        Assert.Equal(2, current.WeatherCode);
        Assert.Equal(18.7, current.WindSpeed10m);
    }

    [Fact]
    public void MapForecasts_zips_rows_to_cities_by_index()
    {
        var rows = new[]
        {
            new Forecast(new Current(23.9, 2, 18.7)),
            new Forecast(new Current(30.1, 0, 5.0)),
            new Forecast(new Current(18.0, 61, 12.3)),
        };

        var items = OpenMeteoWeatherProvider.MapForecasts(rows, Cities);

        Assert.Equal(3, items.Count);
        Assert.Equal("London", items[0].City);
        Assert.Equal(23.9, items[0].TemperatureC);
        Assert.Equal(18.7, items[0].WindKph);
        Assert.Equal("Partly cloudy", items[0].Condition);

        Assert.Equal("New York", items[1].City);
        Assert.Equal("Clear", items[1].Condition);

        Assert.Equal("Tokyo", items[2].City);
        Assert.Equal("Rain", items[2].Condition);
    }

    [Fact]
    public void MapForecasts_shorter_response_yields_fewer_rows()
    {
        var rows = new[] { new Forecast(new Current(23.9, 2, 18.7)) };

        var items = OpenMeteoWeatherProvider.MapForecasts(rows, Cities);

        var item = Assert.Single(items);
        Assert.Equal("London", item.City);
    }

    [Fact]
    public void MapForecasts_defaults_missing_current()
    {
        var rows = new[] { new Forecast(null) };

        var item = Assert.Single(OpenMeteoWeatherProvider.MapForecasts(rows, [Cities[0]]));
        Assert.Equal("London", item.City);
        Assert.Equal(0, item.TemperatureC);
        Assert.Equal(0, item.WindKph);
        Assert.Equal("—", item.Condition);
    }

    [Fact]
    public void MapForecasts_handles_null()
    {
        Assert.Empty(OpenMeteoWeatherProvider.MapForecasts(null, Cities));
    }

    [Theory]
    [InlineData(0, "Clear")]
    [InlineData(2, "Partly cloudy")]
    [InlineData(48, "Fog")]
    [InlineData(55, "Drizzle")]
    [InlineData(65, "Rain")]
    [InlineData(75, "Snow")]
    [InlineData(81, "Showers")]
    [InlineData(86, "Snow showers")]
    [InlineData(99, "Thunderstorm")]
    [InlineData(123, "—")]
    public void WmoToText_maps_known_codes(int code, string expected)
    {
        Assert.Equal(expected, OpenMeteoWeatherProvider.WmoToText(code));
    }

    [Fact]
    public void WmoToText_null_yields_dash()
    {
        Assert.Equal("—", OpenMeteoWeatherProvider.WmoToText(null));
    }
}
