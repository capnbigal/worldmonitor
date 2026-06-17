namespace WorldMonitor.Contracts.Disasters;

public sealed record DisasterAlert
{
    public required string Title { get; init; }
    public required string AlertLevel { get; init; }
    public string? Link { get; init; }
    public long At { get; init; }
}

public sealed record ListDisastersResponse
{
    public IReadOnlyList<DisasterAlert> Items { get; init; } = [];
}
