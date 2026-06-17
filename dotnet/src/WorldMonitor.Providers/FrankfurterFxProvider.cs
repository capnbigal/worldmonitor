using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Fx;

namespace WorldMonitor.Providers;

/// <summary>Foreign-exchange reference rates (ECB) per 1 USD from the public Frankfurter API (no key).
/// Registered as a typed HttpClient with BaseAddress <c>https://api.frankfurter.dev/</c>.</summary>
public interface IFxRateProvider
{
    Task<IReadOnlyList<FxRate>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class FrankfurterFxProvider(HttpClient http) : IFxRateProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<FxRate>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<FxFeed>("v1/latest?base=USD", Json, ct);
        var rates = MapRates(feed);
        return count > 0 && rates.Count > count
            ? rates.Take(count).ToArray()
            : rates;
    }

    /// <summary>Pure mapping (unit-testable). 'rates' is a dictionary of currency code -> rate per 1 USD.
    /// Sorts by currency code (ordinal).</summary>
    public static IReadOnlyList<FxRate> MapRates(FxFeed? feed)
    {
        if (feed?.Rates is null) return [];
        var result = new List<FxRate>(feed.Rates.Count);
        foreach (var kv in feed.Rates)
        {
            result.Add(new FxRate { Currency = kv.Key, Rate = kv.Value });
        }
        result.Sort(static (a, b) => string.CompareOrdinal(a.Currency, b.Currency));
        return result;
    }

    public sealed record FxFeed(
        [property: JsonPropertyName("base")] string? Base,
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("rates")] Dictionary<string, double>? Rates);
}
