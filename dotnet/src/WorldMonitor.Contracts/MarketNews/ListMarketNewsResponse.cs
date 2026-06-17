namespace WorldMonitor.Contracts.MarketNews;

public sealed record ListMarketNewsResponse
{
    public IReadOnlyList<WorldMonitor.Contracts.News.NewsItem> Items { get; init; } = [];

    /// <summary>False when the panel's API key is not configured; the client then shows setup instructions.</summary>
    public bool Configured { get; init; } = true;
}
