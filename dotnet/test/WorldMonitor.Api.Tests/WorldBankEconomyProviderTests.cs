using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public class WorldBankEconomyProviderTests
{
    [Fact]
    public void MapEconomy_skips_null_values_and_sorts_descending()
    {
        // Real shape of the World Bank response: a heterogeneous [meta, data] top-level array. The data array
        // mixes numeric and null 'value' rows; null rows (no data for that year) must be skipped.
        const string json = """
        [
          { "page": 1, "pages": 1, "per_page": 100, "total": 3 },
          [
            { "indicator": { "id": "NY.GDP.MKTP.KD.ZG", "value": "GDP growth (annual %)" },
              "country": { "id": "IN", "value": "India" }, "countryiso3code": "IND",
              "date": "2024", "value": 6.5, "unit": "", "obs_status": "", "decimal": 1 },
            { "indicator": { "id": "NY.GDP.MKTP.KD.ZG", "value": "GDP growth (annual %)" },
              "country": { "id": "CN", "value": "China" }, "countryiso3code": "CHN",
              "date": "2024", "value": 4.977, "unit": "", "obs_status": "", "decimal": 1 },
            { "indicator": { "id": "NY.GDP.MKTP.KD.ZG", "value": "GDP growth (annual %)" },
              "country": { "id": "RU", "value": "Russian Federation" }, "countryiso3code": "RUS",
              "date": "2024", "value": null, "unit": "", "obs_status": "", "decimal": 1 }
          ]
        ]
        """;

        var root = JsonDocument.Parse(json).RootElement;
        var items = WorldBankEconomyProvider.MapEconomy(root);

        Assert.Equal(2, items.Count);                       // the null-value row is skipped
        Assert.Equal("India", items[0].Country);            // 6.5 first (descending)
        Assert.Equal(6.5, items[0].GrowthPercent);
        Assert.Equal("2024", items[0].Year);
        Assert.Equal("China", items[1].Country);            // 4.977 second
        Assert.Equal(4.977, items[1].GrowthPercent);
        Assert.DoesNotContain(items, i => i.Country == "Russian Federation");
    }

    [Fact]
    public void MapEconomy_handles_non_array_and_missing_data()
    {
        Assert.Empty(WorldBankEconomyProvider.MapEconomy(JsonDocument.Parse("{}").RootElement));
        Assert.Empty(WorldBankEconomyProvider.MapEconomy(JsonDocument.Parse("[]").RootElement));
        // [meta, "Invalid value"] — World Bank returns a string in slot [1] on errors, not a data array.
        Assert.Empty(WorldBankEconomyProvider.MapEconomy(
            JsonDocument.Parse("""[{"message":"err"},"Invalid value"]""").RootElement));
    }
}
