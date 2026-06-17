using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.AirQuality;

namespace WorldMonitor.Providers;

/// <summary>European AQI and particulates for a fixed set of major cities from the public Open-Meteo
/// Air Quality API (no key). Registered as a typed HttpClient with BaseAddress
/// <c>https://air-quality-api.open-meteo.com/</c>.</summary>
public interface IAirQualityProvider
{
    Task<IReadOnlyList<CityAirQuality>> FetchAsync(int count = 14, CancellationToken ct = default);
}

public sealed class OpenMeteoAirQualityProvider(HttpClient http) : IAirQualityProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Fixed set of major cities. Open-Meteo preserves input order in its response array,
    /// so results are mapped back to cities by index.</summary>
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

    // The city set is fixed, so the count parameter is ignored; the standard signature is kept.
    public async Task<IReadOnlyList<CityAirQuality>> FetchAsync(int count = 14, CancellationToken ct = default)
    {
        var latitudes = string.Join(',', Cities.Select(c => c.Lat.ToString(CultureInfo.InvariantCulture)));
        var longitudes = string.Join(',', Cities.Select(c => c.Lon.ToString(CultureInfo.InvariantCulture)));
        var rows = await http.GetFromJsonAsync<Forecast[]>(
            $"v1/air-quality?latitude={latitudes}&longitude={longitudes}&current=european_aqi,pm2_5,pm10&timezone=UTC",
            Json, ct);
        return MapForecasts(rows, Cities);
    }

    /// <summary>Pure mapping (unit-testable). Zips upstream rows to cities by index; missing values default to 0.</summary>
    public static IReadOnlyList<CityAirQuality> MapForecasts(Forecast[]? rows, IReadOnlyList<(string City, double Lat, double Lon)> cities)
    {
        var result = new List<CityAirQuality>(cities.Count);
        for (var i = 0; i < cities.Count; i++)
        {
            var current = rows is not null && i < rows.Length ? rows[i].Current : null;
            result.Add(new CityAirQuality
            {
                City = cities[i].City,
                Aqi = current?.EuropeanAqi ?? 0,
                Pm25 = current?.Pm25 ?? 0,
                Pm10 = current?.Pm10 ?? 0,
            });
        }
        return result;
    }

    public sealed record Forecast([property: JsonPropertyName("current")] Current? Current);

    public sealed record Current(
        [property: JsonPropertyName("european_aqi")] int? EuropeanAqi,
        [property: JsonPropertyName("pm2_5")] double? Pm25,
        [property: JsonPropertyName("pm10")] double? Pm10);
}
