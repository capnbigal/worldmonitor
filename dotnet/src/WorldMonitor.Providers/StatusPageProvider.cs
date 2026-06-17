using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Status;

namespace WorldMonitor.Providers;

/// <summary>Aggregates the statuspage.io v2 summary (<c>/api/v2/status.json</c>) for a curated set of major
/// cloud &amp; internet services (no API keys). Registered as a typed HttpClient with no BaseAddress — it
/// fetches absolute status-page URLs.</summary>
public interface IServiceStatusProvider
{
    Task<IReadOnlyList<ServiceStatus>> FetchAsync(int count = 12, CancellationToken ct = default);
}

public sealed class StatusPageProvider(HttpClient http) : IServiceStatusProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Curated, key-free statuspage.io hosts. A failing host is skipped, never fatal.</summary>
    public static readonly IReadOnlyList<(string Service, string Url)> Hosts =
    [
        ("GitHub", "https://www.githubstatus.com/api/v2/status.json"),
        ("Cloudflare", "https://www.cloudflarestatus.com/api/v2/status.json"),
        ("Discord", "https://discordstatus.com/api/v2/status.json"),
        ("OpenAI", "https://status.openai.com/api/v2/status.json"),
        ("Twilio", "https://status.twilio.com/api/v2/status.json"),
        ("DigitalOcean", "https://status.digitalocean.com/api/v2/status.json"),
        ("Dropbox", "https://status.dropbox.com/api/v2/status.json"),
        ("Zoom", "https://status.zoom.us/api/v2/status.json"),
        ("Atlassian", "https://status.atlassian.com/api/v2/status.json"),
    ];

    // Severity ranking: worst-first. Unknown indicators sort lowest (alongside none/maintenance).
    private static int Rank(string indicator) => indicator switch
    {
        "critical" => 3,
        "major" => 2,
        "minor" => 1,
        _ => 0, // none, maintenance, unknown
    };

    public async Task<IReadOnlyList<ServiceStatus>> FetchAsync(int count = 12, CancellationToken ct = default)
    {
        var bag = new ConcurrentBag<ServiceStatus>();
        await Parallel.ForEachAsync(Hosts, ct, async (host, token) =>
        {
            try
            {
                var summary = await http.GetFromJsonAsync<Summary>(host.Url, Json, token);
                bag.Add(MapStatus(host.Service, summary));
            }
            catch
            {
                // Resilient aggregation: a single unreachable / malformed status page must not sink the panel.
            }
        });

        return bag
            .OrderByDescending(s => Rank(s.Indicator))
            .ThenBy(s => s.Service, StringComparer.OrdinalIgnoreCase)
            .Take(count)
            .ToList();
    }

    /// <summary>Pure mapping (unit-testable): one host's deserialized summary + display name into a status row.</summary>
    public static ServiceStatus MapStatus(string service, Summary? summary) => new()
    {
        Service = service,
        Indicator = summary?.Status?.Indicator ?? "unknown",
        Description = summary?.Status?.Description ?? "",
    };

    public sealed record Summary(
        [property: JsonPropertyName("status")] StatusDto? Status);

    public sealed record StatusDto(
        [property: JsonPropertyName("indicator")] string? Indicator,
        [property: JsonPropertyName("description")] string? Description);
}
