using System.Globalization;
using WorldMonitor.Contracts.Fires;

namespace WorldMonitor.Providers;

/// <summary>Active wildfire hotspots (last 24h) from NASA FIRMS, VIIRS S-NPP near-real-time feed.
/// Requires a free (registration-only) MAP_KEY, passed in by the endpoint. Registered as a typed
/// HttpClient with BaseAddress <c>https://firms.modaps.eosdis.nasa.gov/</c>. The feed is CSV, not JSON.</summary>
public interface IFireProvider
{
    Task<IReadOnlyList<FireDetection>> FetchAsync(string apiKey, int count = 100, CancellationToken ct = default);
}

public sealed class NasaFirmsProvider(HttpClient http) : IFireProvider
{
    public async Task<IReadOnlyList<FireDetection>> FetchAsync(string apiKey, int count = 100, CancellationToken ct = default)
    {
        var csv = await http.GetStringAsync($"api/area/csv/{apiKey}/VIIRS_SNPP_NRT/world/1", ct);
        return MapCsv(csv, count);
    }

    /// <summary>Pure mapping (unit-testable): parse the FIRMS CSV by header name (robust to column reordering).
    /// The first non-empty line is the header. Rows whose latitude/longitude don't parse are skipped.
    /// Empty/whitespace or header-only input returns an empty list. At most <paramref name="count"/> rows.</summary>
    public static IReadOnlyList<FireDetection> MapCsv(string csv, int count)
    {
        if (string.IsNullOrWhiteSpace(csv)) return [];

        var lines = csv.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length < 2) return [];

        var header = lines[0].Split(',');
        var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++)
        {
            index[header[i].Trim()] = i;
        }

        var result = new List<FireDetection>();
        for (var r = 1; r < lines.Length && result.Count < count; r++)
        {
            var cells = lines[r].Split(',');

            var lat = Field(cells, index, "latitude");
            var lon = Field(cells, index, "longitude");
            if (!double.TryParse(lat, NumberStyles.Any, CultureInfo.InvariantCulture, out var latitude) ||
                !double.TryParse(lon, NumberStyles.Any, CultureInfo.InvariantCulture, out var longitude))
            {
                continue;
            }

            double.TryParse(Field(cells, index, "bright_ti4"), NumberStyles.Any, CultureInfo.InvariantCulture, out var brightness);
            double.TryParse(Field(cells, index, "frp"), NumberStyles.Any, CultureInfo.InvariantCulture, out var frp);

            result.Add(new FireDetection
            {
                Latitude = latitude,
                Longitude = longitude,
                Brightness = brightness,
                Confidence = Field(cells, index, "confidence"),
                AcqDate = Field(cells, index, "acq_date"),
                Frp = frp,
            });
        }

        return result;
    }

    private static string? Field(string[] cells, IReadOnlyDictionary<string, int> index, string name)
        => index.TryGetValue(name, out var i) && i < cells.Length ? cells[i].Trim() : null;
}
