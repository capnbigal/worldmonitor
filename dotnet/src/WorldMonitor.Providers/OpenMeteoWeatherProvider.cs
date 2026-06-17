using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Weather;

namespace WorldMonitor.Providers;

/// <summary>Current conditions for a fixed set of major cities from the public Open-Meteo API (no key).
/// Registered as a typed HttpClient with BaseAddress <c>https://api.open-meteo.com/</c>.</summary>
public interface IWeatherProvider
{
    Task<IReadOnlyList<CityWeather>> FetchAsync(int count = 14, CancellationToken ct = default);
}

public sealed class OpenMeteoWeatherProvider(HttpClient http) : IWeatherProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Fixed set of major cities. Open-Meteo preserves input order when given CSV coords, so the
    /// response array maps to this list by index.</summary>
    public static readonly IReadOnlyList<(string City, double Lat, double Lon)> Cities =
    [
        ("London", 51.51, -0.13),
        ("New York", 40.71, -74.01),
        ("Tokyo", 35.68, 139.69),
        ("Beijing", 39.90, 116.41),
        ("Delhi", 28.61, 77.21),
        ("Moscow", 55.76, 37.62),
        ("Paris", 48.85, 2.35),
        ("Berlin", 52.52, 13.41),
        ("Sao Paulo", -23.55, -46.63),
        ("Cairo", 30.04, 31.24),
        ("Sydney", -33.87, 151.21),
        ("Los Angeles", 34.05, -118.24),
        ("Dubai", 25.20, 55.27),
        ("Singapore", 1.35, 103.82),
    ];

    // count is ignored: the city set is fixed. Signature kept for orchestrator uniformity.
    public async Task<IReadOnlyList<CityWeather>> FetchAsync(int count = 14, CancellationToken ct = default)
    {
        var latitudes = string.Join(',', Cities.Select(c => c.Lat.ToString(CultureInfo.InvariantCulture)));
        var longitudes = string.Join(',', Cities.Select(c => c.Lon.ToString(CultureInfo.InvariantCulture)));
        Forecast[]? rows;
        try
        {
            rows = await http.GetFromJsonAsync<Forecast[]>(
                $"v1/forecast?latitude={latitudes}&longitude={longitudes}&current=temperature_2m,weather_code,wind_speed_10m&timezone=UTC",
                Json, ct);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            return [];
        }
        return MapForecasts(rows, Cities);
    }

    /// <summary>Pure mapping (unit-testable). Zips the response array to the city list by index; a shorter
    /// response simply yields fewer rows.</summary>
    public static IReadOnlyList<CityWeather> MapForecasts(Forecast[]? rows, IReadOnlyList<(string City, double Lat, double Lon)> cities)
    {
        if (rows is null) return [];
        var result = new List<CityWeather>(Math.Min(rows.Length, cities.Count));
        for (var i = 0; i < cities.Count; i++)
        {
            if (i >= rows.Length) break;
            var current = rows[i].Current;
            result.Add(new CityWeather
            {
                City = cities[i].City,
                TemperatureC = current?.Temperature2m ?? 0,
                WindKph = current?.WindSpeed10m ?? 0,
                Condition = WmoToText(current?.WeatherCode),
            });
        }
        return result;
    }

    /// <summary>WMO weather interpretation code to a short human-readable label.</summary>
    public static string WmoToText(int? code) => code switch
    {
        0 => "Clear",
        >= 1 and <= 3 => "Partly cloudy",
        45 or 48 => "Fog",
        >= 51 and <= 57 => "Drizzle",
        >= 61 and <= 67 => "Rain",
        >= 71 and <= 77 => "Snow",
        >= 80 and <= 82 => "Showers",
        85 or 86 => "Snow showers",
        >= 95 and <= 99 => "Thunderstorm",
        _ => "—",
    };

    public sealed record Forecast(
        [property: JsonPropertyName("current")] Current? Current);

    public sealed record Current(
        [property: JsonPropertyName("temperature_2m")] double? Temperature2m,
        [property: JsonPropertyName("weather_code")] int? WeatherCode,
        [property: JsonPropertyName("wind_speed_10m")] double? WindSpeed10m);
}
