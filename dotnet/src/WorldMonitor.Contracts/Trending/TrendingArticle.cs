namespace WorldMonitor.Contracts.Trending;

public sealed record TrendingArticle
{
    public required string Title { get; init; }
    public long Views { get; init; }
    public string? Description { get; init; }
    public string? Url { get; init; }
}

public sealed record ListTrendingResponse
{
    public IReadOnlyList<TrendingArticle> Items { get; init; } = [];
}
