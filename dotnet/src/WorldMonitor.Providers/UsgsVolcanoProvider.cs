using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Volcano;

namespace WorldMonitor.Providers;

/// <summary>Volcanoes at elevated alert level from the USGS Volcano Hazards Program (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://volcanoes.usgs.gov/</c>.</summary>
public interface IVolcanoProvider
{
    Task<IReadOnlyList<VolcanoAlert>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class UsgsVolcanoProvider(HttpClient http) : IVolcanoProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<VolcanoAlert>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        var rows = await http.GetFromJsonAsync<VolcanoDto[]>(
            "hans-public/api/volcano/getElevatedVolcanoes", Json, ct);
        return MapVolcanoes(rows).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable). Skips rows without a volcano name; newest first.</summary>
    public static IReadOnlyList<VolcanoAlert> MapVolcanoes(VolcanoDto[]? rows)
    {
        if (rows is null) return [];
        var result = new List<VolcanoAlert>(rows.Length);
        foreach (var r in rows)
        {
            if (string.IsNullOrEmpty(r.VolcanoName)) continue;
            result.Add(new VolcanoAlert
            {
                Volcano = r.VolcanoName,
                ColorCode = r.ColorCode ?? "UNKNOWN",
                AlertLevel = r.AlertLevel ?? "",
                Observatory = r.ObsFullname,
                At = (r.SentUnixtime ?? 0) * 1000,
                Url = r.NoticeUrl,
            });
        }
        result.Sort((a, b) => b.At.CompareTo(a.At));
        return result;
    }

    public sealed record VolcanoDto(
        [property: JsonPropertyName("volcano_name")] string? VolcanoName,
        [property: JsonPropertyName("color_code")] string? ColorCode,
        [property: JsonPropertyName("alert_level")] string? AlertLevel,
        [property: JsonPropertyName("obs_fullname")] string? ObsFullname,
        [property: JsonPropertyName("sent_unixtime")] long? SentUnixtime,
        [property: JsonPropertyName("notice_url")] string? NoticeUrl);
}
