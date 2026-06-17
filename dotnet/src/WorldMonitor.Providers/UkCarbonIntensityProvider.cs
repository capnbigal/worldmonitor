using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Energy;

namespace WorldMonitor.Providers;

/// <summary>Live Great Britain electricity generation mix from the public National Grid ESO
/// carbon-intensity API (no key). Registered as a typed HttpClient with BaseAddress
/// <c>https://api.carbonintensity.org.uk/</c>.</summary>
public interface IEnergyMixProvider
{
    Task<IReadOnlyList<GenerationFuel>> FetchAsync(int count = 20, CancellationToken ct = default);
}

public sealed class UkCarbonIntensityProvider(HttpClient http) : IEnergyMixProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<GenerationFuel>> FetchAsync(int count = 20, CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<Feed>("generation", Json, ct);
        var mix = MapMix(feed);
        return count > 0 && mix.Count > count
            ? mix.Take(count).ToArray()
            : mix;
    }

    /// <summary>Pure mapping (unit-testable).</summary>
    public static IReadOnlyList<GenerationFuel> MapMix(Feed? feed)
    {
        var rows = feed?.Data?.GenerationMix;
        if (rows is null) return [];

        var result = new List<GenerationFuel>(rows.Length);
        foreach (var r in rows)
        {
            var fuel = TitleCase(r.Fuel);
            if (string.IsNullOrEmpty(fuel)) continue;
            result.Add(new GenerationFuel
            {
                Fuel = fuel,
                Percent = r.Perc ?? 0,
            });
        }
        result.Sort((a, b) => b.Percent.CompareTo(a.Percent));
        return result;
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(fuel))]
    private static string? TitleCase(string? fuel)
    {
        if (string.IsNullOrEmpty(fuel)) return fuel;
        return char.ToUpper(fuel[0], CultureInfo.InvariantCulture) + fuel[1..];
    }

    public sealed record Feed(
        [property: JsonPropertyName("data")] DataDto? Data);

    public sealed record DataDto(
        [property: JsonPropertyName("generationmix")] Mix[]? GenerationMix);

    public sealed record Mix(
        [property: JsonPropertyName("fuel")] string? Fuel,
        [property: JsonPropertyName("perc")] double? Perc);
}
