using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorldMonitor.Contracts.Security;

namespace WorldMonitor.Providers;

/// <summary>CISA Known Exploited Vulnerabilities catalog (no key). Registered as a typed HttpClient with
/// BaseAddress <c>https://www.cisa.gov/</c>.</summary>
public interface ISecurityAdvisoryProvider
{
    Task<IReadOnlyList<KnownVulnerability>> FetchAsync(int count = 40, CancellationToken ct = default);
}

public sealed class CisaKevProvider(HttpClient http) : ISecurityAdvisoryProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<IReadOnlyList<KnownVulnerability>> FetchAsync(int count = 40, CancellationToken ct = default)
    {
        Catalog? catalog;
        try
        {
            catalog = await http.GetFromJsonAsync<Catalog>(
                "sites/default/files/feeds/known_exploited_vulnerabilities.json", Json, ct);
        }
        catch (JsonException)
        {
            return [];
        }

        var all = MapCatalog(catalog);
        if (all.Count <= count) return all;
        return all.Take(count).ToList();
    }

    /// <summary>Pure mapping (unit-testable). Skips entries without a CVE id, parses dates with the
    /// invariant culture, and sorts newest-added first.</summary>
    public static IReadOnlyList<KnownVulnerability> MapCatalog(Catalog? c)
    {
        if (c?.Vulnerabilities is null) return [];
        var result = new List<KnownVulnerability>(c.Vulnerabilities.Length);
        foreach (var v in c.Vulnerabilities)
        {
            if (string.IsNullOrEmpty(v.CveId)) continue;
            result.Add(new KnownVulnerability
            {
                CveId = v.CveId,
                VendorProject = v.VendorProject ?? "",
                Product = (v.Product ?? "").Trim(),
                Name = v.VulnerabilityName ?? "",
                DateAdded = ParseDate(v.DateAdded),
                ShortDescription = v.ShortDescription,
                KnownRansomware = v.KnownRansomwareCampaignUse == "Known",
                DueDate = ParseDate(v.DueDate),
            });
        }
        result.Sort((a, b) => Nullable.Compare(b.DateAdded, a.DateAdded));
        return result;
    }

    private static DateTime? ParseDate(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public sealed record Catalog(
        [property: JsonPropertyName("vulnerabilities")] Vuln[]? Vulnerabilities);

    public sealed record Vuln(
        [property: JsonPropertyName("cveID")] string? CveId,
        [property: JsonPropertyName("vendorProject")] string? VendorProject,
        [property: JsonPropertyName("product")] string? Product,
        [property: JsonPropertyName("vulnerabilityName")] string? VulnerabilityName,
        [property: JsonPropertyName("dateAdded")] string? DateAdded,
        [property: JsonPropertyName("shortDescription")] string? ShortDescription,
        [property: JsonPropertyName("knownRansomwareCampaignUse")] string? KnownRansomwareCampaignUse,
        [property: JsonPropertyName("dueDate")] string? DueDate);
}
