namespace WorldMonitor.Contracts.AirQuality;

public sealed record CityAirQuality
{
    public required string City { get; init; }
    public int Aqi { get; init; }
    public double Pm25 { get; init; }
    public double Pm10 { get; init; }
}

public sealed record ListAirQualityResponse
{
    public IReadOnlyList<CityAirQuality> Items { get; init; } = [];
}
