using System.ServiceModel.Syndication;
using System.Xml;
using WorldMonitor.Contracts.Disasters;

namespace WorldMonitor.Providers;

/// <summary>Global disaster alerts (floods, cyclones, earthquakes, volcanoes…) from the GDACS RSS feed (no key).
/// Registered as a typed HttpClient with BaseAddress <c>https://www.gdacs.org/</c>.</summary>
public interface IDisasterProvider
{
    Task<IReadOnlyList<DisasterAlert>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class GdacsDisasterProvider(HttpClient http) : IDisasterProvider
{
    public async Task<IReadOnlyList<DisasterAlert>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        var xml = await http.GetStringAsync("xml/rss.xml", ct);
        return MapFeed(xml).Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable): parse the GDACS RSS XML into disaster alerts. The alert level
    /// (Green/Orange/Red) is the first whitespace-delimited word of the title; anything else is "Unknown".</summary>
    public static IReadOnlyList<DisasterAlert> MapFeed(string xml)
    {
        SyndicationFeed feed;
        using (var reader = XmlReader.Create(new StringReader(xml)))
            feed = SyndicationFeed.Load(reader);

        var items = new List<DisasterAlert>();
        foreach (var item in feed.Items)
        {
            var title = item.Title?.Text?.Trim();
            if (string.IsNullOrEmpty(title)) continue;

            var published = item.PublishDate.Year > 1
                ? item.PublishDate
                : item.LastUpdatedTime;

            items.Add(new DisasterAlert
            {
                Title = title,
                AlertLevel = ExtractLevel(title),
                Link = item.Links.FirstOrDefault()?.Uri?.ToString(),
                At = published.Year > 1 ? published.ToUnixTimeMilliseconds() : 0,
            });
        }
        return items;
    }

    private static string ExtractLevel(string title)
    {
        var first = title.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        if (first.Equals("Green", StringComparison.OrdinalIgnoreCase)) return "Green";
        if (first.Equals("Orange", StringComparison.OrdinalIgnoreCase)) return "Orange";
        if (first.Equals("Red", StringComparison.OrdinalIgnoreCase)) return "Red";
        return "Unknown";
    }
}
