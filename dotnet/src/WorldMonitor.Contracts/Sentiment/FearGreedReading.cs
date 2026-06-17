namespace WorldMonitor.Contracts.Sentiment;

public sealed record FearGreedReading
{
    public int Value { get; init; }
    public required string Classification { get; init; }
    public long At { get; init; }
}

public sealed record ListFearGreedResponse
{
    public IReadOnlyList<FearGreedReading> Items { get; init; } = [];
}
