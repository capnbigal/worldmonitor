namespace WorldMonitor.Contracts.Core;

public sealed record PaginationResponse
{
    public string NextCursor { get; init; } = "";
    public int TotalCount { get; init; }
}
