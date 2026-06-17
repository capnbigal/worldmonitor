using System.Collections.Concurrent;
using System.ServiceModel.Syndication;
using System.Text.RegularExpressions;
using System.Xml;
using WorldMonitor.Contracts.News;

namespace WorldMonitor.Providers;

/// <summary>Aggregates world-news headlines from a curated set of public RSS/Atom feeds (no API keys).
/// Registered as a typed HttpClient with no BaseAddress — it fetches absolute feed URLs.</summary>
public interface INewsProvider
{
    Task<IReadOnlyList<NewsItem>> FetchHeadlinesAsync(int count = 60, CancellationToken ct = default);
}

public sealed partial class RssNewsProvider(HttpClient http) : INewsProvider
{
    /// <summary>Curated, key-free world-news feeds. A failing feed is skipped, never fatal.</summary>
    public static readonly IReadOnlyList<(string Source, string Url)> Feeds =
    [
        ("BBC World", "https://feeds.bbci.co.uk/news/world/rss.xml"),
        ("The Guardian", "https://www.theguardian.com/world/rss"),
        ("NPR", "https://feeds.npr.org/1001/rss.xml"),
        ("PBS NewsHour", "https://www.pbs.org/newshour/feeds/rss/headlines"),
        ("France 24", "https://www.france24.com/en/rss"),
        ("Deutsche Welle", "https://rss.dw.com/xml/rss-en-all"),
        ("Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml"),
        ("Euronews", "https://www.euronews.com/rss?format=xml"),
    ];

    public async Task<IReadOnlyList<NewsItem>> FetchHeadlinesAsync(int count = 60, CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<NewsItem>();
        await Parallel.ForEachAsync(Feeds, ct, async (feed, token) =>
        {
            try
            {
                var xml = await http.GetStringAsync(feed.Url, token);
                foreach (var item in MapFeed(feed.Source, xml))
                    bag.Add(item);
            }
            catch
            {
                // Resilient aggregation: a single unreachable / malformed feed must not sink the panel.
            }
        });

        return Merge(bag, count);
    }

    /// <summary>Pure mapping (unit-testable): parse one feed's XML into news items.</summary>
    public static IReadOnlyList<NewsItem> MapFeed(string source, string xml)
    {
        SyndicationFeed feed;
        using (var reader = XmlReader.Create(new StringReader(xml)))
            feed = SyndicationFeed.Load(reader);

        var items = new List<NewsItem>();
        foreach (var item in feed.Items)
        {
            var link = item.Links.FirstOrDefault()?.Uri?.ToString();
            var title = item.Title?.Text?.Trim();
            if (string.IsNullOrEmpty(link) || string.IsNullOrEmpty(title)) continue;

            var published = item.PublishDate.Year > 1
                ? item.PublishDate
                : item.LastUpdatedTime;

            items.Add(new NewsItem
            {
                Id = string.IsNullOrEmpty(item.Id) ? link : item.Id,
                Title = CleanText(title),
                Summary = CleanText((item.Summary?.Text).Limit(280)),
                Link = link,
                Source = source,
                PublishedAt = published.Year > 1 ? published.ToUnixTimeMilliseconds() : 0,
            });
        }
        return items;
    }

    /// <summary>De-duplicate by link, then by normalized title; newest first; cap at <paramref name="count"/>.</summary>
    public static IReadOnlyList<NewsItem> Merge(IEnumerable<NewsItem> items, int count)
    {
        var seenLinks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<NewsItem>();
        foreach (var item in items.OrderByDescending(i => i.PublishedAt))
        {
            if (!seenLinks.Add(item.Link)) continue;
            if (!seenTitles.Add(item.Title)) continue;
            result.Add(item);
            if (result.Count >= count) break;
        }
        return result;
    }

    [return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(nameof(raw))]
    private static string? CleanText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return raw;
        var noTags = HtmlTag().Replace(raw, " ");
        return WebUtilityDecodeCollapse(noTags);
    }

    private static string WebUtilityDecodeCollapse(string s)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(s);
        return Whitespace().Replace(decoded, " ").Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex HtmlTag();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}

internal static class StringLimitExtensions
{
    public static string? Limit(this string? s, int max) =>
        s is null || s.Length <= max ? s : s[..max];
}
