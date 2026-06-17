using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.CoinGeckoProvider.CoinDto;

namespace WorldMonitor.Api.Tests;

public class CoinGeckoProviderTests
{
    [Fact]
    public void CoinDto_binds_coingecko_snake_case_fields()
    {
        // Shape returned by /api/v3/coins/markets. Guards the naming-policy gotcha:
        // JsonNamingPolicy.SnakeCaseLower maps to "price_change_percentage24h" (no underscore before 24)
        // and would silently leave the value null. Explicit [JsonPropertyName] fixes that.
        const string json = """
        [{"id":"bitcoin","symbol":"btc","name":"Bitcoin","image":"https://img/btc.png",
          "current_price":65586,"market_cap":1314780412427,"price_change_percentage_24h":-1.02}]
        """;

        var dtos = JsonSerializer.Deserialize<Dto[]>(json)!;
        var coins = CoinGeckoProvider.MapCoins(dtos);

        var c = Assert.Single(coins);
        Assert.Equal("bitcoin", c.Id);
        Assert.Equal("BTC", c.Symbol);
        Assert.Equal(65586, c.Price);
        Assert.Equal(1314780412427, c.MarketCap);
        Assert.Equal(-1.02, c.ChangePercent24h);          // the field that previously bound to null
        Assert.Equal("https://img/btc.png", c.ImageUrl);
    }

    [Fact]
    public void MapCoins_maps_fields_and_uppercases_symbol()
    {
        var coins = CoinGeckoProvider.MapCoins(
        [
            new Dto("bitcoin", "btc", "Bitcoin", "https://img/btc.png", 65000.5, 1_280_000_000_000, 2.34),
        ]);

        var c = Assert.Single(coins);
        Assert.Equal("bitcoin", c.Id);
        Assert.Equal("BTC", c.Symbol);            // uppercased
        Assert.Equal("Bitcoin", c.Name);
        Assert.Equal(65000.5, c.Price);
        Assert.Equal(2.34, c.ChangePercent24h);
        Assert.Equal(1_280_000_000_000, c.MarketCap);
        Assert.Equal("https://img/btc.png", c.ImageUrl);
    }

    [Fact]
    public void MapCoins_defaults_nulls_and_skips_coins_without_id()
    {
        var coins = CoinGeckoProvider.MapCoins(
        [
            new Dto(null, "x", "X", null, null, null, null),         // skipped — no id
            new Dto("eth", null, null, null, null, null, null),      // defaults applied
        ]);

        var c = Assert.Single(coins);
        Assert.Equal("eth", c.Id);
        Assert.Equal("", c.Symbol);
        Assert.Equal("", c.Name);
        Assert.Equal(0, c.Price);
        Assert.Null(c.ChangePercent24h);
        Assert.Equal(0, c.MarketCap);
        Assert.Null(c.ImageUrl);
    }

    [Fact]
    public void MapCoins_handles_null()
    {
        Assert.Empty(CoinGeckoProvider.MapCoins(null));
    }
}
