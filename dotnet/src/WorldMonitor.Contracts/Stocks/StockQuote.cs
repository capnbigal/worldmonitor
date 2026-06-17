namespace WorldMonitor.Contracts.Stocks;

public sealed record StockQuote
{
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public double Price { get; init; }
    public double Change { get; init; }
    public double? ChangePercent { get; init; }
}

public sealed record ListStocksResponse
{
    public IReadOnlyList<StockQuote> Items { get; init; } = [];

    /// <summary>False when the panel's API key is not configured; the client then shows setup instructions.</summary>
    public bool Configured { get; init; } = true;
}
