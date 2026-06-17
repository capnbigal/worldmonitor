namespace WorldMonitor.Contracts.Weather;

public sealed record CityWeather
{
    public required string City { get; init; }
    public double TemperatureC { get; init; }
    public double WindKph { get; init; }
    public required string Condition { get; init; }
}

public sealed record ListWeatherResponse
{
    public IReadOnlyList<CityWeather> Items { get; init; } = [];
}
