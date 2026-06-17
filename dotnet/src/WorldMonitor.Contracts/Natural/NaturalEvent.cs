namespace WorldMonitor.Contracts.Natural;

public sealed record NaturalEvent
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Category { get; init; }
    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public DateTime? Date { get; init; }
    public string? Link { get; init; }
}

public sealed record ListNaturalEventsResponse
{
    public IReadOnlyList<NaturalEvent> Events { get; init; } = [];
}
