using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Dto = WorldMonitor.Providers.FinnhubStockProvider.QuoteDto;

namespace WorldMonitor.Api.Tests;

public class FinnhubStockProviderTests
{
    [Fact]
    public void QuoteDto_binds_finnhub_quote_fields()
    {
        const string json = """
        { "c":227.5, "d":1.23, "dp":0.54, "h":228.9, "l":225.1, "o":226.0, "pc":226.27, "t":1781700000 }
        """;
        var dto = JsonSerializer.Deserialize<Dto>(json);

        var quote = FinnhubStockProvider.MapQuote("AAPL", "Apple", dto);

        Assert.NotNull(quote);
        Assert.Equal("AAPL", quote!.Symbol);
        Assert.Equal("Apple", quote.Name);
        Assert.Equal(227.5, quote.Price);
        Assert.Equal(1.23, quote.Change);
        Assert.Equal(0.54, quote.ChangePercent);
    }

    [Fact]
    public void MapQuote_defaults_missing_change_to_zero()
    {
        var quote = FinnhubStockProvider.MapQuote("MSFT", "Microsoft", new Dto(415.0, null, null));

        Assert.NotNull(quote);
        Assert.Equal(415.0, quote!.Price);
        Assert.Equal(0, quote.Change);
        Assert.Null(quote.ChangePercent);
    }

    [Fact]
    public void MapQuote_returns_null_for_no_data_or_null()
    {
        // Finnhub returns c == 0 for a symbol with no data → skipped.
        Assert.Null(FinnhubStockProvider.MapQuote("XXXX", "No Data", new Dto(0, 0, 0)));
        Assert.Null(FinnhubStockProvider.MapQuote("XXXX", "No Data", new Dto(null, null, null)));
        Assert.Null(FinnhubStockProvider.MapQuote("XXXX", "No Data", null));
    }

    [Fact]
    public void Symbols_are_nonempty_with_symbols_and_names()
    {
        Assert.NotEmpty(FinnhubStockProvider.Symbols);
        Assert.All(FinnhubStockProvider.Symbols, s =>
        {
            Assert.False(string.IsNullOrWhiteSpace(s.Symbol));
            Assert.False(string.IsNullOrWhiteSpace(s.Name));
        });
    }
}
