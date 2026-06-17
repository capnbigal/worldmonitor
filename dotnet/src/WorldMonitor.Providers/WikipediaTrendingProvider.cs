using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Trending;

namespace WorldMonitor.Providers;

/// <summary>Most-read English Wikipedia articles for the previous day from the public Wikimedia REST
/// feed (no key). Registered as a typed HttpClient with BaseAddress <c>https://en.wikipedia.org/</c>;
/// the required descriptive User-Agent is set at DI time.</summary>
public interface IWikipediaTrendingProvider
{
    Task<IReadOnlyList<TrendingArticle>> FetchAsync(int count = 30, CancellationToken ct = default);
}

public sealed class WikipediaTrendingProvider(HttpClient http) : IWikipediaTrendingProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<TrendingArticle>> FetchAsync(int count = 30, CancellationToken ct = default)
    {
        // Use yesterday (UTC): today's "most-read" feed may be incomplete.
        var day = DateTime.UtcNow.AddDays(-1);
        var path = string.Format(
            CultureInfo.InvariantCulture,
            "api/rest_v1/feed/featured/{0:yyyy}/{0:MM}/{0:dd}",
            day);

        var feed = await http.GetFromJsonAsync<Feed>(path, Json, ct);
        return MapArticles(feed).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable). Keeps upstream order (already ranked by views);
    /// skips entries without a usable title.</summary>
    public static IReadOnlyList<TrendingArticle> MapArticles(Feed? feed)
    {
        var articles = feed?.MostRead?.Articles;
        if (articles is null) return [];
        var result = new List<TrendingArticle>(articles.Length);
        foreach (var a in articles)
        {
            var title = a.Titles?.Normalized ?? a.Title;
            if (string.IsNullOrEmpty(title)) continue;
            result.Add(new TrendingArticle
            {
                Title = title,
                Views = a.Views ?? 0,
                Description = a.Description,
                Url = a.ContentUrls?.Desktop?.Page,
            });
        }
        return result;
    }

    public sealed record Feed(
        [property: JsonPropertyName("mostread")] MostRead? MostRead);

    public sealed record MostRead(
        [property: JsonPropertyName("articles")] Article[]? Articles);

    public sealed record Article(
        [property: JsonPropertyName("views")] long? Views,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("titles")] TitlesDto? Titles,
        [property: JsonPropertyName("description")] string? Description,
        [property: JsonPropertyName("content_urls")] ContentUrls? ContentUrls);

    public sealed record TitlesDto(
        [property: JsonPropertyName("normalized")] string? Normalized);

    public sealed record ContentUrls(
        [property: JsonPropertyName("desktop")] LinkDto? Desktop);

    public sealed record LinkDto(
        [property: JsonPropertyName("page")] string? Page);
}
