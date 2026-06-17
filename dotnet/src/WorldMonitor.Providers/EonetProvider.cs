using System.Net.Http.Json;
using System.Text.Json;
using WorldMonitor.Contracts.Natural;

namespace WorldMonitor.Providers;

/// <summary>Open natural events (wildfires, storms, volcanoes…) from NASA EONET (no key). Registered as a
/// typed HttpClient with BaseAddress <c>https://eonet.gsfc.nasa.gov/</c>.</summary>
public interface INaturalEventProvider
{
    Task<IReadOnlyList<NaturalEvent>> FetchAsync(int count = 30, CancellationToken ct = default);
}

public sealed class EonetProvider(HttpClient http) : INaturalEventProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<NaturalEvent>> FetchAsync(int count = 30, CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<EonetFeed>($"api/v3/events?status=open&limit={count}", Json, ct);
        return MapEvents(feed);
    }

    /// <summary>Pure mapping (unit-testable). Uses the most recent geometry; only Point geometries carry coordinates.</summary>
    public static IReadOnlyList<NaturalEvent> MapEvents(EonetFeed? feed)
    {
        if (feed?.Events is null) return [];
        var result = new List<NaturalEvent>(feed.Events.Length);
        foreach (var e in feed.Events)
        {
            var geom = e.Geometry is { Length: > 0 } g ? g[^1] : null; // latest geometry
            double? lat = null, lon = null;
            if (geom?.Type == "Point" && geom.Coordinates is { ValueKind: JsonValueKind.Array } coords && coords.GetArrayLength() >= 2)
            {
                lon = coords[0].GetDouble();
                lat = coords[1].GetDouble();
            }
            result.Add(new NaturalEvent
            {
                Id = e.Id,
                Title = e.Title,
                Category = e.Categories is { Length: > 0 } cats ? cats[0].Title : null,
                Latitude = lat,
                Longitude = lon,
                Date = geom?.Date,
                Link = e.Link,
            });
        }
        return result;
    }

    public sealed record EonetFeed(EonetEvent[]? Events);
    public sealed record EonetEvent(string Id, string Title, string? Link, EonetCategory[]? Categories, EonetGeom[]? Geometry);
    public sealed record EonetCategory(string? Id, string? Title);
    // Coordinates kept as JsonElement so a Polygon event (nested arrays) doesn't break deserialization.
    public sealed record EonetGeom(DateTime? Date, string? Type, JsonElement? Coordinates);
}
