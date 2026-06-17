using System.Collections.Concurrent;
using WorldMonitor.Contracts.News;

namespace WorldMonitor.Providers;

/// <summary>Region-scoped world-news headlines aggregated from public RSS feeds (no keys). Reuses
/// <see cref="RssNewsProvider"/>'s feed parsing and merge; only the per-region feed list differs.
/// Registered as a typed HttpClient with no BaseAddress — it fetches absolute feed URLs.</summary>
public interface IRegionalNewsProvider
{
    /// <summary>Ordered (key, display-name) pairs for the supported regions.</summary>
    IReadOnlyList<(string Key, string Name)> Regions { get; }

    Task<IReadOnlyList<NewsItem>> FetchAsync(string region, int count = 40, CancellationToken ct = default);
}

public sealed class RegionalNewsProvider(HttpClient http) : IRegionalNewsProvider
{
    /// <summary>Curated, key-free regional feeds. A failing feed is skipped, never fatal.</summary>
    public static readonly IReadOnlyList<(string Key, string Name, (string Source, string Url)[] Feeds)> RegionFeeds =
    [
        ("europe", "Europe",
        [
            ("BBC", "https://feeds.bbci.co.uk/news/world/europe/rss.xml"),
            ("France 24", "https://www.france24.com/en/europe/rss"),
        ]),
        ("middle-east", "Middle East",
        [
            ("BBC", "https://feeds.bbci.co.uk/news/world/middle_east/rss.xml"),
            ("France 24", "https://www.france24.com/en/middle-east/rss"),
            ("Al Jazeera", "https://www.aljazeera.com/xml/rss/all.xml"),
        ]),
        ("asia", "Asia",
        [
            ("BBC", "https://feeds.bbci.co.uk/news/world/asia/rss.xml"),
            ("France 24", "https://www.france24.com/en/asia-pacific/rss"),
        ]),
        ("africa", "Africa",
        [
            ("BBC", "https://feeds.bbci.co.uk/news/world/africa/rss.xml"),
            ("France 24", "https://www.france24.com/en/africa/rss"),
        ]),
        ("americas", "Americas",
        [
            ("BBC", "https://feeds.bbci.co.uk/news/world/us_and_canada/rss.xml"),
            ("BBC", "https://feeds.bbci.co.uk/news/world/latin_america/rss.xml"),
            ("France 24", "https://www.france24.com/en/americas/rss"),
        ]),
    ];

    public IReadOnlyList<(string Key, string Name)> Regions =>
        RegionFeeds.Select(r => (r.Key, r.Name)).ToList();

    public async Task<IReadOnlyList<NewsItem>> FetchAsync(string region, int count = 40, CancellationToken ct = default)
    {
        var match = RegionFeeds.FirstOrDefault(r => r.Key.Equals(region, StringComparison.OrdinalIgnoreCase));
        if (match.Feeds is null) return [];   // unknown region

        var bag = new ConcurrentBag<NewsItem>();
        await Parallel.ForEachAsync(match.Feeds, ct, async (feed, token) =>
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
