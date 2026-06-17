using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Tech;

namespace WorldMonitor.Providers;

/// <summary>Top stories from the public Hacker News (Firebase) API (no key). Registered as a typed
/// HttpClient with BaseAddress <c>https://hacker-news.firebaseio.com/</c>. Fetches the ranked list of
/// story ids, then each item concurrently while preserving the upstream rank order.</summary>
public interface IHackerNewsProvider
{
    Task<IReadOnlyList<HackerNewsStory>> FetchAsync(int count = 30, CancellationToken ct = default);
}

public sealed class HackerNewsProvider(HttpClient http) : IHackerNewsProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<HackerNewsStory>> FetchAsync(int count = 30, CancellationToken ct = default)
    {
        var ids = await http.GetFromJsonAsync<long[]>("v0/topstories.json", Json, ct);
        if (ids is null || ids.Length == 0) return [];

        var ranked = ids.Take(count).Select((id, index) => (index, id)).ToArray();

        // Fetch items concurrently but keep (index, story) so we can restore rank order afterwards;
        // completion order is non-deterministic. A failed item is skipped, never fatal for the panel.
        var fetched = new (int Index, HackerNewsStory? Story)[ranked.Length];
        await Parallel.ForEachAsync(ranked, ct, async (entry, token) =>
        {
            HackerNewsStory? story = null;
            try
            {
                var dto = await http.GetFromJsonAsync<ItemDto>($"v0/item/{entry.id}.json", Json, token);
                story = MapStory(dto);
            }
            catch
            {
                // Resilient aggregation: a single unreachable / malformed item must not sink the panel.
            }
            fetched[entry.index] = (entry.index, story);
        });

        var result = new List<HackerNewsStory>(ranked.Length);
        foreach (var (_, story) in fetched.OrderBy(f => f.Index))
        {
            if (story is not null) result.Add(story);
        }
        return result;
    }

    /// <summary>Pure mapping (unit-testable): a null dto or one without a title yields null so the
    /// caller can skip it. Missing comment/score/time default to 0; time is epoch seconds → ms.</summary>
    public static HackerNewsStory? MapStory(ItemDto? dto)
    {
        if (dto is null || string.IsNullOrEmpty(dto.Title)) return null;
        return new HackerNewsStory
        {
            Id = dto.Id ?? 0,
            Title = dto.Title,
            Url = dto.Url,
            Score = dto.Score ?? 0,
            By = dto.By,
            Comments = dto.Descendants ?? 0,
            At = (dto.Time ?? 0) * 1000,
        };
    }

    public sealed record ItemDto(
        [property: JsonPropertyName("id")] long? Id,
        [property: JsonPropertyName("title")] string? Title,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("score")] int? Score,
        [property: JsonPropertyName("by")] string? By,
        [property: JsonPropertyName("descendants")] int? Descendants,
        [property: JsonPropertyName("time")] long? Time);
}
