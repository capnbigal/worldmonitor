using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Market;

namespace WorldMonitor.Providers;

/// <summary>Top cryptocurrencies by market cap from the public CoinGecko API (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://api.coingecko.com/</c>.</summary>
public interface IMarketProvider
{
    Task<IReadOnlyList<CoinQuote>> FetchTopCoinsAsync(int count = 20, CancellationToken ct = default);
}

public sealed class CoinGeckoProvider(HttpClient http) : IMarketProvider
{
    // CoinGecko uses snake_case JSON. Field names are mapped explicitly on CoinDto rather than via
    // JsonNamingPolicy.SnakeCaseLower, because that policy maps PriceChangePercentage24h to
    // "price_change_percentage24h" (no underscore before the digit) and silently fails to bind the
    // real field "price_change_percentage_24h".
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<CoinQuote>> FetchTopCoinsAsync(int count = 20, CancellationToken ct = default)
    {
        var coins = await http.GetFromJsonAsync<CoinDto[]>(
            $"api/v3/coins/markets?vs_currency=usd&order=market_cap_desc&per_page={count}&page=1", Json, ct);
        return MapCoins(coins);
    }

    /// <summary>Pure mapping (unit-testable).</summary>
    public static IReadOnlyList<CoinQuote> MapCoins(CoinDto[]? coins)
    {
        if (coins is null) return [];
        var result = new List<CoinQuote>(coins.Length);
        foreach (var c in coins)
        {
            if (c.Id is null) continue;
            result.Add(new CoinQuote
            {
                Id = c.Id,
                Symbol = (c.Symbol ?? "").ToUpperInvariant(),
                Name = c.Name ?? "",
                Price = c.CurrentPrice ?? 0,
                ChangePercent24h = c.PriceChangePercentage24h,
                MarketCap = c.MarketCap ?? 0,
                ImageUrl = c.Image,
            });
        }
        return result;
    }

    public sealed record CoinDto(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("image")] string? Image,
        [property: JsonPropertyName("current_price")] double? CurrentPrice,
        [property: JsonPropertyName("market_cap")] long? MarketCap,
        [property: JsonPropertyName("price_change_percentage_24h")] double? PriceChangePercentage24h);
}
