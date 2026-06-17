namespace WorldMonitor.Contracts.Status;

public sealed record ServiceStatus
{
    public required string Service { get; init; }
    public required string Indicator { get; init; }
    public required string Description { get; init; }
}

public sealed record ListServiceStatusResponse
{
    public IReadOnlyList<ServiceStatus> Items { get; init; } = [];
}
