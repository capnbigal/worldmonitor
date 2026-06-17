namespace WorldMonitor.Contracts.Economy;

public sealed record EconomyIndicator
{
    public required string Country { get; init; }
    public double GrowthPercent { get; init; }
    public string? Year { get; init; }
}

public sealed record ListEconomyResponse
{
    public IReadOnlyList<EconomyIndicator> Items { get; init; } = [];
}
