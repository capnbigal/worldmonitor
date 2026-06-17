using WorldMonitor.Contracts.Core;

namespace WorldMonitor.Contracts.Seismology;

public sealed record Earthquake
{
    public required string Id { get; init; }
    public string Place { get; init; } = "";
    public double Magnitude { get; init; }
    public double DepthKm { get; init; }
    public GeoCoordinates? Location { get; init; }
    public long OccurredAt { get; init; }              // INT64_ENCODING_NUMBER -> JSON number
    public string SourceUrl { get; init; } = "";
    public bool? NearTestSite { get; init; }
    public string? TestSiteName { get; init; }
    public double? ConcernScore { get; init; }
    public string? ConcernLevel { get; init; }
}
