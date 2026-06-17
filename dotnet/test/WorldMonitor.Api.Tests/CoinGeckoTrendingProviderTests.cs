using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.CoinGeckoTrendingProvider.Feed;

namespace WorldMonitor.Api.Tests;

public class CoinGeckoTrendingProviderTests
{
    [Fact]
    public void Feed_binds_coingecko_trending_shape()
    {
        // Shape returned by /api/v3/search/trending. Each coin is wrapped in {"item": {...}} and the 24h
        // change is nested under a per-currency object keyed by "usd".
        const string json = """
        {"coins":[
          {"item":{"id":"plasma","name":"Plasma","symbol":"XPL","market_cap_rank":138,
            "data":{"price":0.11693,"price_change_percentage_24h":{"usd":29.38,"eur":27.1}}}}
        ],"nfts":[],"categories":[]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var coins = CoinGeckoTrendingProvider.MapCoins(feed);

        var c = Assert.Single(coins);
        Assert.Equal("Plasma", c.Name);
        Assert.Equal("XPL", c.Symbol);
        Assert.Equal(138, c.MarketCapRank);
        Assert.Equal(0.11693, c.Price);
        Assert.Equal(29.38, c.ChangePercent24h);
    }

    [Fact]
    public void MapCoins_uppercases_symbol_and_reads_usd_change()
    {
        const string json = """
        {"coins":[{"item":{"name":"Bitcoin","symbol":"btc","market_cap_rank":1,
          "data":{"price":65000.5,"price_change_percentage_24h":{"usd":2.34}}}}]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var coins = CoinGeckoTrendingProvider.MapCoins(feed);

        var c = Assert.Single(coins);
        Assert.Equal("Bitcoin", c.Name);
        Assert.Equal("BTC", c.Symbol);          // uppercased
        Assert.Equal(1, c.MarketCapRank);
        Assert.Equal(65000.5, c.Price);
        Assert.Equal(2.34, c.ChangePercent24h);
    }

    [Fact]
    public void MapCoins_defaults_missing_data_and_skips_blank_names()
    {
        const string json = """
        {"coins":[
          {"item":{"name":"","symbol":"x"}},
          {"item":{"name":"NoData","symbol":"nd"}},
          {"item":null}
        ]}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var coins = CoinGeckoTrendingProvider.MapCoins(feed);

        var c = Assert.Single(coins);
        Assert.Equal("NoData", c.Name);
        Assert.Equal("ND", c.Symbol);
        Assert.Null(c.MarketCapRank);
        Assert.Equal(0, c.Price);
        Assert.Null(c.ChangePercent24h);
    }

    [Fact]
    public void MapCoins_handles_null_feed_and_null_coins()
    {
        Assert.Empty(CoinGeckoTrendingProvider.MapCoins(null));
        Assert.Empty(CoinGeckoTrendingProvider.MapCoins(new Feed(null)));
    }
}
