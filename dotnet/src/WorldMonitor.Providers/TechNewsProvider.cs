using System.Collections.Concurrent;
using WorldMonitor.Contracts.News;

namespace WorldMonitor.Providers;

/// <summary>Technology &amp; AI headlines aggregated from a curated set of public RSS/Atom feeds (no keys).
/// Reuses <see cref="RssNewsProvider"/>'s feed parsing and merge; only the feed list differs. Registered as a
/// typed HttpClient with no BaseAddress — it fetches absolute feed URLs.</summary>
public interface ITechNewsProvider
{
    Task<IReadOnlyList<NewsItem>> FetchHeadlinesAsync(int count = 50, CancellationToken ct = default);
}

public sealed class TechNewsProvider(HttpClient http) : ITechNewsProvider
{
    /// <summary>Curated, key-free technology / AI feeds. A failing feed is skipped, never fatal.</summary>
    public static readonly IReadOnlyList<(string Source, string Url)> Feeds =
    [
        ("Ars Technica", "https://feeds.arstechnica.com/arstechnica/index"),
        ("The Verge", "https://www.theverge.com/rss/index.xml"),
        ("TechCrunch", "https://techcrunch.com/feed/"),
        ("Wired", "https://www.wired.com/feed/rss"),
        ("MIT Technology Review", "https://www.technologyreview.com/feed/"),
        ("The Register", "https://www.theregister.com/headlines.atom"),
        ("Engadget", "https://www.engadget.com/rss.xml"),
        ("Hacker News", "https://hnrss.org/frontpage"),
    ];

    public async Task<IReadOnlyList<NewsItem>> FetchHeadlinesAsync(int count = 50, CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<NewsItem>();
        await Parallel.ForEachAsync(Feeds, ct, async (feed, token) =>
        {
            try
            {
                var xml = await http.GetStringAsync(feed.Url, token);
                foreach (var item in RssNewsProvider.MapFeed(feed.Source, xml))
                    bag.Add(item);
            }
            catch
            {
                // Resilient aggregation: a single unreachable / malformed feed must not sink the panel.
            }
        });

        return RssNewsProvider.Merge(bag, count);
    }
}
