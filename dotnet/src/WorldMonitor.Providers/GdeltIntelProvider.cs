using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Intel;

namespace WorldMonitor.Providers;

/// <summary>Worldwide news coverage from the public GDELT DOC 2.0 API (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://api.gdeltproject.org/</c>.</summary>
public interface IIntelProvider
{
    Task<IReadOnlyList<IntelArticle>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class GdeltIntelProvider(HttpClient http) : IIntelProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // GDELT's "seendate" is a compact UTC timestamp like 20260617T120000Z.
    private const string SeenDateFormat = "yyyyMMdd'T'HHmmss'Z'";

    private const string Query = "geopolitics OR conflict OR sanctions";

    public async Task<IReadOnlyList<IntelArticle>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        // GDELT is flaky: it can return an empty body, JSON without an "articles" key, or non-JSON/HTML
        // on error. Treat any failure as "no data" rather than throwing.
        try
        {
            var url = $"api/v2/doc/doc?query={Uri.EscapeDataString(Query)}&mode=ArtList&format=json&maxrecords={count}&sort=DateDesc";
            var feed = await http.GetFromJsonAsync<Feed>(url, Json, ct);
            return MapArticles(feed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // GDELT rate-limits aggressively (HTTP 429 with a plain-text body) and can return non-JSON on
            // error; treat any non-cancellation failure as "no data" so the panel degrades gracefully.
            return [];
        }
    }

    /// <summary>Pure mapping (unit-testable). Skips items missing a title or url; keeps API order.</summary>
    public static IReadOnlyList<IntelArticle> MapArticles(Feed? feed)
    {
        if (feed?.Articles is null) return [];
        var result = new List<IntelArticle>(feed.Articles.Length);
        foreach (var a in feed.Articles)
        {
            if (string.IsNullOrEmpty(a.Title) || string.IsNullOrEmpty(a.Url)) continue;
            result.Add(new IntelArticle
            {
                Title = a.Title,
                Url = a.Url,
                Domain = a.Domain,
                Language = a.Language,
                SourceCountry = a.SourceCountry,
                SeenAt = ParseSeenDate(a.SeenDate),
            });
        }
        return result;
    }

    private static long ParseSeenDate(string? seenDate)
    {
        if (string.IsNullOrEmpty(seenDate)) return 0;
        return DateTimeOffset.TryParseExact(
                seenDate, SeenDateFormat, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dto)
            ? dto.ToUnixTimeMilliseconds()
            : 0;
    }

    public sealed record Feed(
        [property: JsonPropertyName("articles")] Article[]? Articles);

    public sealed record Article(
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("seendate")] string? SeenDate,
        [property: JsonPropertyName("domain")] string? Domain,
        [property: JsonPropertyName("language")] string? Language,
        [property: JsonPropertyName("sourcecountry")] string? SourceCountry);
}
