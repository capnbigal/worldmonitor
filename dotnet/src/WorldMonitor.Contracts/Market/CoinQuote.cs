namespace WorldMonitor.Contracts.Market;

public sealed record CoinQuote
{
    public required string Id { get; init; }
    public required string Symbol { get; init; }
    public required string Name { get; init; }
    public double Price { get; init; }
    public double? ChangePercent24h { get; init; }
    public long MarketCap { get; init; }
    public string? ImageUrl { get; init; }
}

public sealed record ListCoinsResponse
{
    public IReadOnlyList<CoinQuote> Coins { get; init; } = [];
}
