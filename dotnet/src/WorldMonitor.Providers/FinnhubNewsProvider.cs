using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.News;

namespace WorldMonitor.Providers;

/// <summary>Latest general financial-market headlines from the Finnhub news API. Requires a free
/// (registration-only) API key, passed in by the endpoint. Registered as a typed HttpClient with
/// BaseAddress <c>https://finnhub.io/</c>.</summary>
public interface IMarketNewsProvider
{
    Task<IReadOnlyList<WorldMonitor.Contracts.News.NewsItem>> FetchAsync(string apiKey, int count = 40, CancellationToken ct = default);
}

public sealed class FinnhubNewsProvider(HttpClient http) : IMarketNewsProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NewsItem>> FetchAsync(string apiKey, int count = 40, CancellationToken ct = default)
    {
        var items = await http.GetFromJsonAsync<NewsDto[]>(
            $"api/v1/news?category=general&token={apiKey}", Json, ct);
        return MapNews(items).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable): skips rows with an empty headline or url, dedupes by link,
    /// preserves upstream order. datetime is epoch seconds → ms.</summary>
    public static IReadOnlyList<NewsItem> MapNews(NewsDto[]? items)
    {
        if (items is null) return [];
        var result = new List<NewsItem>(items.Length);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var n in items)
        {
            if (string.IsNullOrEmpty(n.Headline) || string.IsNullOrEmpty(n.Url)) continue;
            if (!seen.Add(n.Url)) continue;
            result.Add(new NewsItem
            {
                Id = n.Id?.ToString(CultureInfo.InvariantCulture) ?? n.Url,
                Title = n.Headline,
                Summary = n.Summary,
                Link = n.Url,
                Source = n.Source ?? "",
                PublishedAt = (n.Datetime ?? 0) * 1000,
            });
        }
        return result;
    }

    public sealed record NewsDto(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("headline")] string? Headline,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("source")] string? Source,
        [property: JsonPropertyName("datetime")] long? Datetime);
}
