namespace WorldMonitor.Contracts.Market;

public sealed record TrendingCoin
{
    public required string Name { get; init; }
    public required string Symbol { get; init; }
    public int? MarketCapRank { get; init; }
    public double Price { get; init; }
    public double? ChangePercent24h { get; init; }
}

public sealed record ListTrendingCoinsResponse
{
    public IReadOnlyList<TrendingCoin> Items { get; init; } = [];
}
