namespace WorldMonitor.Contracts.Volcano;

public sealed record VolcanoAlert
{
    public required string Volcano { get; init; }
    public required string ColorCode { get; init; }
    public required string AlertLevel { get; init; }
    public string? Observatory { get; init; }
    public long At { get; init; }
    public string? Url { get; init; }
}

public sealed record ListVolcanoAlertsResponse
{
    public IReadOnlyList<VolcanoAlert> Items { get; init; } = [];
}
