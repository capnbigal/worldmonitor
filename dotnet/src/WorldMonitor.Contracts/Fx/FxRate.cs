namespace WorldMonitor.Contracts.Fx;

public sealed record FxRate
{
    public required string Currency { get; init; }
    public double Rate { get; init; }
}

public sealed record ListFxRatesResponse
{
    public IReadOnlyList<FxRate> Items { get; init; } = [];
}
