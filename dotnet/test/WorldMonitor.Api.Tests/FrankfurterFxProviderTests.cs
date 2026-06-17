using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Feed = WorldMonitor.Providers.FrankfurterFxProvider.FxFeed;

namespace WorldMonitor.Api.Tests;

public class FrankfurterFxProviderTests
{
    [Fact]
    public void FxFeed_binds_frankfurter_fields_and_dictionary_rates()
    {
        // Shape returned by /v1/latest?base=USD: 'rates' is a dictionary of currency code -> rate per 1 USD.
        const string json = """
        {"amount":1.0,"base":"USD","date":"2026-06-16",
         "rates":{"EUR":0.86252,"GBP":0.74583,"CAD":1.4014,"AUD":1.4155}}
        """;

        var feed = JsonSerializer.Deserialize<Feed>(json)!;
        var rates = FrankfurterFxProvider.MapRates(feed);

        Assert.Equal(4, rates.Count);
        // Sorted by currency code (ordinal): AUD, CAD, EUR, GBP
        Assert.Collection(rates,
            r => { Assert.Equal("AUD", r.Currency); Assert.Equal(1.4155, r.Rate); },
            r => { Assert.Equal("CAD", r.Currency); Assert.Equal(1.4014, r.Rate); },
            r => { Assert.Equal("EUR", r.Currency); Assert.Equal(0.86252, r.Rate); },
            r => { Assert.Equal("GBP", r.Currency); Assert.Equal(0.74583, r.Rate); });
    }

    [Fact]
    public void MapRates_sorts_by_currency_ordinal()
    {
        var feed = new Feed("USD", "2026-06-16", new Dictionary<string, double>
        {
            ["JPY"] = 150.0,
            ["BRL"] = 5.0603,
            ["CHF"] = 0.79558,
        });

        var rates = FrankfurterFxProvider.MapRates(feed);

        Assert.Equal(["BRL", "CHF", "JPY"], rates.Select(r => r.Currency));
    }

    [Fact]
    public void MapRates_handles_null_feed()
    {
        Assert.Empty(FrankfurterFxProvider.MapRates(null));
    }

    [Fact]
    public void MapRates_handles_null_rates()
    {
        Assert.Empty(FrankfurterFxProvider.MapRates(new Feed("USD", "2026-06-16", null)));
    }
}
