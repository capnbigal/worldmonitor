using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.WeatherAlerts;

namespace WorldMonitor.Providers;

/// <summary>Active severe &amp; extreme US weather alerts from the National Weather Service (NWS / NOAA, no key).
/// Registered as a typed HttpClient with BaseAddress <c>https://api.weather.gov/</c> and a descriptive
/// User-Agent set at DI time (api.weather.gov rejects requests without one).</summary>
public interface IWeatherAlertProvider
{
    Task<IReadOnlyList<WeatherAlert>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class NwsAlertProvider(HttpClient http) : IWeatherAlertProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<WeatherAlert>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<Feed>(
            "alerts/active?status=actual&severity=Extreme,Severe", Json, ct);
        return MapAlerts(feed).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable). Newest (by <c>sent</c>) first.</summary>
    public static IReadOnlyList<WeatherAlert> MapAlerts(Feed? feed)
    {
        if (feed?.Features is null) return [];
        var result = new List<WeatherAlert>(feed.Features.Length);
        foreach (var feature in feed.Features)
        {
            var props = feature.Properties;
            if (props?.Event is null) continue;
            result.Add(new WeatherAlert
            {
                Event = props.Event,
                Severity = props.Severity ?? "",
                Area = props.AreaDesc,
                Headline = props.Headline,
                At = DateTimeOffset.TryParse(props.Sent, CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var dto) ? dto.ToUnixTimeMilliseconds() : 0,
            });
        }
        result.Sort((a, b) => b.At.CompareTo(a.At));
        return result;
    }

    public sealed record Feed([property: JsonPropertyName("features")] Feature[]? Features);
    public sealed record Feature([property: JsonPropertyName("properties")] Props? Properties);
    public sealed record Props(
        [property: JsonPropertyName("event")] string? Event,
        [property: JsonPropertyName("severity")] string? Severity,
        [property: JsonPropertyName("headline")] string? Headline,
        [property: JsonPropertyName("areaDesc")] string? AreaDesc,
        [property: JsonPropertyName("sent")] string? Sent);
}
