using System.Net.Http.Json;
using System.Text.Json;
using WorldMonitor.Contracts.Economy;

namespace WorldMonitor.Providers;

/// <summary>Latest available GDP growth (annual %, indicator NY.GDP.MKTP.KD.ZG) for major economies from the
/// public World Bank API (no key). Registered as a typed HttpClient with BaseAddress
/// <c>https://api.worldbank.org/</c>.</summary>
public interface IEconomyProvider
{
    Task<IReadOnlyList<EconomyIndicator>> FetchAsync(int count = 20, CancellationToken ct = default);
}

public sealed class WorldBankEconomyProvider(HttpClient http) : IEconomyProvider
{
    // Major economies by GDP, queried in one request joined by ';'. mrv=1 returns the most recent value per country.
    private const string Countries = "USA;CHN;JPN;DEU;IND;GBR;FRA;BRA;ITA;CAN;RUS;KOR;AUS;ESP;MEX;IDN;TUR;SAU;NLD;CHE";

    public async Task<IReadOnlyList<EconomyIndicator>> FetchAsync(int count = 20, CancellationToken ct = default)
    {
        // The World Bank response is a heterogeneous top-level array: [0] is a metadata object, [1] is the data
        // array. It cannot be deserialized into a single typed record, so it is read as a raw JsonElement.
        var root = await http.GetFromJsonAsync<JsonElement>(
            $"v2/country/{Countries}/indicator/NY.GDP.MKTP.KD.ZG?format=json&mrv=1&per_page=100", ct);
        var items = MapEconomy(root);
        return count < items.Count ? items.Take(count).ToList() : items;
    }

    /// <summary>Pure mapping (unit-testable). The top-level JSON is a [meta, data] array; only the data array
    /// (element [1]) is read. Rows whose value is null (no data) or whose country name is null are skipped.
    /// Sorted by GDP growth descending.</summary>
    public static IReadOnlyList<EconomyIndicator> MapEconomy(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() < 2) return [];

        var data = root[1];
        if (data.ValueKind != JsonValueKind.Array) return [];

        var result = new List<EconomyIndicator>(data.GetArrayLength());
        foreach (var item in data.EnumerateArray())
        {
            if (!item.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Number) continue;

            var country = item.GetProperty("country").GetProperty("value").GetString();
            if (country is null) continue;

            result.Add(new EconomyIndicator
            {
                Country = country,
                GrowthPercent = value.GetDouble(),
                Year = item.GetProperty("date").GetString(),
            });
        }

        result.Sort((a, b) => b.GrowthPercent.CompareTo(a.GrowthPercent));
        return result;
    }
}
