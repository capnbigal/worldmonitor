namespace WorldMonitor.Contracts.Intel;

public sealed record IntelArticle
{
    public required string Title { get; init; }
    public required string Url { get; init; }
    public string? Domain { get; init; }
    public string? Language { get; init; }
    public string? SourceCountry { get; init; }
    public long SeenAt { get; init; }
}

public sealed record ListIntelResponse
{
    public IReadOnlyList<IntelArticle> Items { get; init; } = [];
}
