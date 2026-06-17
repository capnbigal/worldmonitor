namespace WorldMonitor.Contracts.Displacement;

public sealed record DisplacementByCountry
{
    public required string Country { get; init; }
    public long Refugees { get; init; }
    public long AsylumSeekers { get; init; }
    public long Idps { get; init; }
}

public sealed record ListDisplacementResponse
{
    public IReadOnlyList<DisplacementByCountry> Items { get; init; } = [];
}
