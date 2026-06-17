using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Sentiment;

namespace WorldMonitor.Providers;

/// <summary>Crypto Fear &amp; Greed Index from the public alternative.me API (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://api.alternative.me/</c>.</summary>
public interface IMarketSentimentProvider
{
    Task<IReadOnlyList<FearGreedReading>> FetchAsync(int count = 30, CancellationToken ct = default);
}

public sealed class FearGreedProvider(HttpClient http) : IMarketSentimentProvider
{
    // alternative.me returns numeric value and epoch timestamp as JSON STRINGS in snake_case
    // ("value":"22","value_classification":"Extreme Fear","timestamp":"1781654400"). Field names are
    // mapped explicitly rather than via JsonNamingPolicy.SnakeCaseLower, and the strings are parsed in
    // MapReadings with InvariantCulture (items that fail to parse are skipped).
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FearGreedReading>> FetchAsync(int count = 30, CancellationToken ct = default)
    {
        try
        {
            var feed = await http.GetFromJsonAsync<Feed>($"fng/?limit={count}", Json, ct);
            return MapReadings(feed);
        }
        catch (JsonException)
        {
            return [];
        }
    }

    /// <summary>Pure mapping (unit-testable). The API already returns newest-first; order is preserved.
    /// Value and timestamp arrive as strings; readings with an unparseable value are skipped.</summary>
    public static IReadOnlyList<FearGreedReading> MapReadings(Feed? feed)
    {
        if (feed?.Data is null) return [];
        var result = new List<FearGreedReading>(feed.Data.Length);
        foreach (var r in feed.Data)
        {
            if (!int.TryParse(r.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)) continue;
            long at = 0;
            if (long.TryParse(r.Timestamp, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds))
                at = seconds * 1000;
            result.Add(new FearGreedReading
            {
                Value = value,
                Classification = r.Classification ?? "",
                At = at,
            });
        }
        return result;
    }

    public sealed record Feed([property: JsonPropertyName("data")] Reading[]? Data);

    public sealed record Reading(
        [property: JsonPropertyName("value")] string? Value,
        [property: JsonPropertyName("value_classification")] string? Classification,
        [property: JsonPropertyName("timestamp")] string? Timestamp);
}
