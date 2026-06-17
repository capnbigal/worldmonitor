using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Macro;

namespace WorldMonitor.Providers;

/// <summary>Latest values for a curated set of US macro/financial series from the St. Louis Fed FRED API.
/// Requires a free (registration-only) API key, passed in by the endpoint. Registered as a typed HttpClient
/// with BaseAddress <c>https://api.stlouisfed.org/</c>.</summary>
public interface IMacroProvider
{
    Task<IReadOnlyList<MacroIndicator>> FetchAsync(string apiKey, int count = 20, CancellationToken ct = default);
}

public sealed class FredMacroProvider(HttpClient http) : IMacroProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Curated FRED series: (series id, display name, units).</summary>
    public static readonly IReadOnlyList<(string Id, string Name, string Units)> Series =
    [
        ("DFF", "Federal Funds Rate", "%"),
        ("DGS2", "2-Year Treasury", "%"),
        ("DGS10", "10-Year Treasury", "%"),
        ("T10Y2Y", "10Y–2Y Spread", "%"),
        ("UNRATE", "Unemployment Rate", "%"),
        ("CPIAUCSL", "CPI (Index)", "index"),
        ("VIXCLS", "VIX", "index"),
        ("DEXUSEU", "USD per EUR", "rate"),
    ];

    public async Task<IReadOnlyList<MacroIndicator>> FetchAsync(string apiKey, int count = 20, CancellationToken ct = default)
    {
        var take = Series.Take(count).Select((s, i) => (i, s)).ToArray();
        var fetched = new (int Index, MacroIndicator? Indicator)[take.Length];

        await Parallel.ForEachAsync(take, ct, async (entry, token) =>
        {
            MacroIndicator? indicator = null;
            try
            {
                var dto = await http.GetFromJsonAsync<ObservationsDto>(
                    $"fred/series/observations?series_id={entry.s.Id}&api_key={apiKey}&file_type=json&sort_order=desc&limit=1",
                    Json, token);
                var value = ParseLatest(dto);
                if (value is { } v)
                {
                    indicator = new MacroIndicator
                    {
                        Name = entry.s.Name,
                        SeriesId = entry.s.Id,
                        Value = v,
                        Date = dto?.Observations?.FirstOrDefault()?.Date,
                        Units = entry.s.Units,
                    };
                }
            }
            catch
            {
                // Resilient: a single failing series must not sink the panel.
            }
            fetched[entry.i] = (entry.i, indicator);
        });

        return fetched.OrderBy(f => f.Index).Select(f => f.Indicator).OfType<MacroIndicator>().ToList();
    }

    /// <summary>Pure mapping (unit-testable): the latest observation value, or null when missing ("." in FRED).</summary>
    public static double? ParseLatest(ObservationsDto? dto)
    {
        var raw = dto?.Observations?.FirstOrDefault()?.Value;
        return double.TryParse(raw, NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : null;
    }

    public sealed record ObservationsDto(
        [property: JsonPropertyName("observations")] ObservationDto[]? Observations);

    public sealed record ObservationDto(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("value")] string? Value);
}
