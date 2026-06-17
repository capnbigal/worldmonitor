using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Stocks;

namespace WorldMonitor.Providers;

/// <summary>Latest quotes for a curated set of major US equities from the Finnhub API. Requires a free
/// (registration-only) API key, passed in by the endpoint. Registered as a typed HttpClient with
/// BaseAddress <c>https://finnhub.io/</c>. Fetches each symbol concurrently while preserving list order.</summary>
public interface IStockProvider
{
    Task<IReadOnlyList<StockQuote>> FetchAsync(string apiKey, int count = 15, CancellationToken ct = default);
}

public sealed class FinnhubStockProvider(HttpClient http) : IStockProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Curated equities: (ticker symbol, display name).</summary>
    public static readonly IReadOnlyList<(string Symbol, string Name)> Symbols =
    [
        ("AAPL", "Apple"),
        ("MSFT", "Microsoft"),
        ("GOOGL", "Alphabet"),
        ("AMZN", "Amazon"),
        ("NVDA", "Nvidia"),
        ("META", "Meta Platforms"),
        ("TSLA", "Tesla"),
        ("JPM", "JPMorgan Chase"),
        ("V", "Visa"),
        ("WMT", "Walmart"),
        ("XOM", "ExxonMobil"),
        ("JNJ", "Johnson & Johnson"),
    ];

    public async Task<IReadOnlyList<StockQuote>> FetchAsync(string apiKey, int count = 15, CancellationToken ct = default)
    {
        var take = Symbols.Take(count).Select((s, i) => (i, s)).ToArray();

        // Fetch quotes concurrently but keep (index, quote) so we can restore list order afterwards;
        // completion order is non-deterministic. A failed / no-data symbol is skipped, never fatal.
        var fetched = new (int Index, StockQuote? Quote)[take.Length];
        await Parallel.ForEachAsync(take, ct, async (entry, token) =>
        {
            StockQuote? quote = null;
            try
            {
                var dto = await http.GetFromJsonAsync<QuoteDto>(
                    $"api/v1/quote?symbol={entry.s.Symbol}&token={apiKey}", Json, token);
                quote = MapQuote(entry.s.Symbol, entry.s.Name, dto);
            }
            catch
            {
                // Resilient aggregation: a single unreachable / malformed quote must not sink the panel.
            }
            fetched[entry.i] = (entry.i, quote);
        });

        var result = new List<StockQuote>(take.Length);
        foreach (var (_, quote) in fetched.OrderBy(f => f.Index))
        {
            if (quote is not null) result.Add(quote);
        }
        return result;
    }

    /// <summary>Pure mapping (unit-testable): a null dto, or one whose current price (c) is null or 0
    /// (Finnhub's "no data" sentinel), yields null so the caller can skip the symbol.</summary>
    public static StockQuote? MapQuote(string symbol, string name, QuoteDto? dto)
    {
        if (dto?.C is not { } price || price == 0) return null;
        return new StockQuote
        {
            Symbol = symbol,
            Name = name,
            Price = price,
            Change = dto.D ?? 0,
            ChangePercent = dto.Dp,
        };
    }

    public sealed record QuoteDto(
        [property: JsonPropertyName("c")] double? C,
        [property: JsonPropertyName("d")] double? D,
        [property: JsonPropertyName("dp")] double? Dp);
}
