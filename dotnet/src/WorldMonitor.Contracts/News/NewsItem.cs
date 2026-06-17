namespace WorldMonitor.Contracts.News;

public sealed record NewsItem
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Summary { get; init; }
    public required string Link { get; init; }
    public required string Source { get; init; }
    /// <summary>Publication time in Unix epoch milliseconds (0 when the feed item carries no date).</summary>
    public long PublishedAt { get; init; }
}

public sealed record ListNewsResponse
{
    public IReadOnlyList<NewsItem> Items { get; init; } = [];
}
