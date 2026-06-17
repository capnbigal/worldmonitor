namespace WorldMonitor.Contracts.Core;

public sealed record GeoCoordinates
{
    public double Latitude { get; init; }
    public double Longitude { get; init; }
}
