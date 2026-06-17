using System.Net.Http.Json;
using System.Text.Json;
using WorldMonitor.Contracts.Core;
using WorldMonitor.Contracts.Seismology;

namespace WorldMonitor.Providers;

/// <summary>Fetches recent earthquakes from the public USGS GeoJSON feed (no API key required) and maps
/// them to the shared <see cref="Earthquake"/> DTO. Registered as a typed HttpClient with BaseAddress
/// <c>https://earthquake.usgs.gov/</c>.</summary>
public sealed class UsgsEarthquakeProvider(HttpClient http) : IEarthquakeProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // "all_day" = every M0+ event in the past 24 hours.
    private const string FeedPath = "earthquakes/feed/v1.0/summary/all_day.geojson";

    public async Task<IReadOnlyList<Earthquake>> FetchAsync(CancellationToken ct = default)
    {
        var feed = await http.GetFromJsonAsync<UsgsFeed>(FeedPath, Json, ct);
        return MapFeed(feed);
    }

    /// <summary>Pure mapping from the USGS GeoJSON shape to the Earthquake DTO (unit-testable).</summary>
    public static IReadOnlyList<Earthquake> MapFeed(UsgsFeed? feed)
    {
        if (feed?.Features is null) return [];
        var result = new List<Earthquake>(feed.Features.Length);
        foreach (var f in feed.Features)
        {
            if (f.Properties is null || f.Geometry?.Coordinates is not { Length: >= 3 } c) continue;
            result.Add(new Earthquake
            {
                Id = f.Id,
                Place = f.Properties.Place ?? "",
                Magnitude = f.Properties.Mag ?? 0,
                DepthKm = c[2],
                Location = new GeoCoordinates { Latitude = c[1], Longitude = c[0] },
                OccurredAt = f.Properties.Time,
                SourceUrl = f.Properties.Url ?? "",
            });
        }
        return result;
    }

    // Minimal subset of the USGS GeoJSON FeatureCollection.
    public sealed record UsgsFeed(UsgsFeature[]? Features);
    public sealed record UsgsFeature(string Id, UsgsProps? Properties, UsgsGeom? Geometry);
    public sealed record UsgsProps(string? Place, double? Mag, long Time, string? Url);
    public sealed record UsgsGeom(double[]? Coordinates); // [longitude, latitude, depthKm]
}
