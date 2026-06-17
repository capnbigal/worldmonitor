namespace WorldMonitor.Contracts.WeatherAlerts;

public sealed record WeatherAlert
{
    public required string Event { get; init; }
    public required string Severity { get; init; }
    public string? Area { get; init; }
    public string? Headline { get; init; }
    public long At { get; init; }
}

public sealed record ListWeatherAlertsResponse
{
    public IReadOnlyList<WeatherAlert> Items { get; init; } = [];
}
