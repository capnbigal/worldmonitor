using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Space;

namespace WorldMonitor.Providers;

/// <summary>Planetary K-index (geomagnetic activity) from NOAA SWPC (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://services.swpc.noaa.gov/</c>.</summary>
public interface ISpaceWeatherProvider
{
    Task<IReadOnlyList<KpReading>> FetchAsync(int count = 24, CancellationToken ct = default);
}

public sealed class NoaaSpaceWeatherProvider(HttpClient http) : ISpaceWeatherProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<KpReading>> FetchAsync(int count = 24, CancellationToken ct = default)
    {
        KpDto[]? rows;
        try
        {
            rows = await http.GetFromJsonAsync<KpDto[]>("products/noaa-planetary-k-index.json", Json, ct);
        }
        catch (JsonException)
        {
            return [];
        }
        return MapReadings(rows, count);
    }

    /// <summary>Pure mapping (unit-testable). Upstream rows are chronological (oldest first); returns the
    /// most recent <paramref name="count"/> readings in newest-first order.</summary>
    public static IReadOnlyList<KpReading> MapReadings(KpDto[]? rows, int count = 24)
    {
        if (rows is null) return [];
        var parsed = new List<KpReading>(rows.Length);
        foreach (var r in rows)
        {
            if (r.TimeTag is null) continue;
            // time_tag is UTC with no zone suffix; parse and force UTC kind.
            if (!DateTime.TryParse(r.TimeTag, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
            {
                continue;
            }
            if (dt.Kind == DateTimeKind.Unspecified)
            {
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            var at = new DateTimeOffset(dt).ToUnixTimeMilliseconds();
            parsed.Add(new KpReading { At = at, Kp = r.Kp ?? 0 });
        }

        // Take the tail (most recent) and return newest-first.
        var take = count < 0 ? 0 : count;
        var start = Math.Max(0, parsed.Count - take);
        var result = new List<KpReading>(parsed.Count - start);
        for (var i = parsed.Count - 1; i >= start; i--)
        {
            result.Add(parsed[i]);
        }
        return result;
    }

    public sealed record KpDto(
        [property: JsonPropertyName("time_tag")] string? TimeTag,
        [property: JsonPropertyName("Kp")] double? Kp);
}
