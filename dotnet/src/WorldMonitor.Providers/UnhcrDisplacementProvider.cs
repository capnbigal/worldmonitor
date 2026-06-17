using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Displacement;

namespace WorldMonitor.Providers;

/// <summary>Forced-displacement totals (refugees, asylum-seekers, IDPs) by country of origin from the
/// public UNHCR population API (no key). Registered as a typed HttpClient with BaseAddress
/// <c>https://api.unhcr.org/</c>.</summary>
public interface IDisplacementProvider
{
    Task<IReadOnlyList<DisplacementByCountry>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class UnhcrDisplacementProvider(HttpClient http) : IDisplacementProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<DisplacementByCountry>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        // The API returns coo-aggregated rows (coa_name == "-") when coo_all=true.
        var feed = await http.GetFromJsonAsync<Feed>(
            $"population/v1/population/?limit={count}&yearFrom=2024&yearTo=2024&coo_all=true", Json, ct);
        return MapCountries(feed);
    }

    /// <summary>Pure mapping (unit-testable). Skips placeholder/unknown origins, parses the mixed
    /// number/string counts, and sorts by total displaced population descending.</summary>
    public static IReadOnlyList<DisplacementByCountry> MapCountries(Feed? feed)
    {
        if (feed?.Items is null) return [];
        var result = new List<DisplacementByCountry>(feed.Items.Length);
        foreach (var item in feed.Items)
        {
            var country = item.CooName;
            if (string.IsNullOrEmpty(country) || country is "-" or "Unknown") continue;
            result.Add(new DisplacementByCountry
            {
                Country = country,
                Refugees = ReadLong(item.Refugees),
                AsylumSeekers = ReadLong(item.AsylumSeekers),
                Idps = ReadLong(item.Idps),
            });
        }
        result.Sort((a, b) => (b.Refugees + b.AsylumSeekers + b.Idps).CompareTo(a.Refugees + a.AsylumSeekers + a.Idps));
        return result;
    }

    /// <summary>UNHCR returns these counts as a JSON number (5766586) OR a JSON string ("0"); handle both.</summary>
    private static long ReadLong(JsonElement? element) => element switch
    {
        { ValueKind: JsonValueKind.Number } e => e.GetInt64(),
        { ValueKind: JsonValueKind.String } e =>
            long.TryParse(e.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0,
        _ => 0,
    };

    public sealed record Feed(
        [property: JsonPropertyName("items")] Item[]? Items);

    public sealed record Item(
        [property: JsonPropertyName("coo_name")] string? CooName,
        [property: JsonPropertyName("coo")] string? Coo,
        [property: JsonPropertyName("refugees")] JsonElement? Refugees,
        [property: JsonPropertyName("asylum_seekers")] JsonElement? AsylumSeekers,
        [property: JsonPropertyName("idps")] JsonElement? Idps);
}
