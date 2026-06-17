namespace WorldMonitor.Contracts.Tech;

public sealed record HackerNewsStory
{
    public long Id { get; init; }
    public required string Title { get; init; }
    public string? Url { get; init; }
    public int Score { get; init; }
    public string? By { get; init; }
    public int Comments { get; init; }
    public long At { get; init; }
}

public sealed record ListHackerNewsResponse
{
    public IReadOnlyList<HackerNewsStory> Items { get; init; } = [];
}
