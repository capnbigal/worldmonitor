using WorldMonitor.Contracts.Core;

namespace WorldMonitor.Contracts.Seismology;

public sealed record ListEarthquakesResponse
{
    public IReadOnlyList<Earthquake> Earthquakes { get; init; } = [];
    public PaginationResponse? Pagination { get; init; }
}
