using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Market;

namespace WorldMonitor.Providers;

/// <summary>Most-searched ("trending") coins from the public CoinGecko API (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://api.coingecko.com/</c>.</summary>
public interface ITrendingCryptoProvider
{
    Task<IReadOnlyList<TrendingCoin>> FetchAsync(int count = 15, CancellationToken ct = default);
}

public sealed class CoinGeckoTrendingProvider(HttpClient http) : ITrendingCryptoProvider
{
    // CoinGecko uses snake_case JSON and nests the 24h change under a per-currency object. Field names
    // are mapped explicitly rather than via JsonNamingPolicy.SnakeCaseLower, which mangles names like
    // "price_change_percentage_24h" (no underscore before the digit) and silently fails to bind.
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TrendingCoin>> FetchAsync(int count = 15, CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<Feed>("api/v3/search/trending", Json, ct);
        return MapCoins(feed).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable).</summary>
    public static IReadOnlyList<TrendingCoin> MapCoins(Feed? feed)
    {
        if (feed?.Coins is null) return [];
        var result = new List<TrendingCoin>(feed.Coins.Length);
        foreach (var wrap in feed.Coins)
        {
            var item = wrap.Item;
            if (item is null) continue;
            if (string.IsNullOrEmpty(item.Name) || string.IsNullOrEmpty(item.Symbol)) continue;
            result.Add(new TrendingCoin
            {
                Name = item.Name,
                Symbol = item.Symbol.ToUpperInvariant(),
                MarketCapRank = item.MarketCapRank,
                Price = item.Data?.Price ?? 0,
                ChangePercent24h = item.Data?.PriceChangePercentage24h?.Usd,
            });
        }
        return result;
    }

    public sealed record Feed(
        [property: JsonPropertyName("coins")] CoinWrap[]? Coins);

    public sealed record CoinWrap(
        [property: JsonPropertyName("item")] ItemDto? Item);

    public sealed record ItemDto(
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("symbol")] string? Symbol,
        [property: JsonPropertyName("market_cap_rank")] int? MarketCapRank,
        [property: JsonPropertyName("data")] CoinData? Data);

    public sealed record CoinData(
        [property: JsonPropertyName("price")] double? Price,
        [property: JsonPropertyName("price_change_percentage_24h")] Change? PriceChangePercentage24h);

    public sealed record Change(
        [property: JsonPropertyName("usd")] double? Usd);
}
