using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Catalog = WorldMonitor.Providers.CisaKevProvider.Catalog;
using Vuln = WorldMonitor.Providers.CisaKevProvider.Vuln;

namespace WorldMonitor.Api.Tests;

public class CisaKevProviderTests
{
    [Fact]
    public void Catalog_binds_cisa_camel_case_fields()
    {
        // Shape returned by the CISA KEV feed. Guards the embedded-capital field "cveID":
        // a naming policy would map CveId to "cveId" and silently leave it null.
        const string json = """
        {
          "title":"CISA Catalog of Known Exploited Vulnerabilities",
          "catalogVersion":"2026.06.16",
          "count":1,
          "vulnerabilities":[
            {"cveID":"CVE-2026-48907","vendorProject":"Widget Factory","product":"Joomla Content Editor ",
             "vulnerabilityName":"Widget Factory Content Injection Vulnerability","dateAdded":"2026-06-16",
             "shortDescription":"A content injection flaw.","requiredAction":"Apply mitigations.",
             "dueDate":"2026-07-07","knownRansomwareCampaignUse":"Known","notes":"https://example/notes"}
          ]
        }
        """;

        var catalog = JsonSerializer.Deserialize<Catalog>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        var items = CisaKevProvider.MapCatalog(catalog);

        var v = Assert.Single(items);
        Assert.Equal("CVE-2026-48907", v.CveId);     // the embedded-capital field that previously bound to null
        Assert.Equal("Widget Factory", v.VendorProject);
        Assert.Equal("Joomla Content Editor", v.Product);    // trailing space trimmed
        Assert.Equal("Widget Factory Content Injection Vulnerability", v.Name);
        Assert.Equal(new DateTime(2026, 6, 16), v.DateAdded);
        Assert.Equal("A content injection flaw.", v.ShortDescription);
        Assert.True(v.KnownRansomware);
        Assert.Equal(new DateTime(2026, 7, 7), v.DueDate);
    }

    [Fact]
    public void MapCatalog_sorts_by_date_added_descending()
    {
        var catalog = new Catalog(
        [
            new Vuln("CVE-1", "V1", "P1", "Old", "2026-01-01", null, "Unknown", null),
            new Vuln("CVE-2", "V2", "P2", "New", "2026-06-01", null, "Unknown", null),
            new Vuln("CVE-3", "V3", "P3", "Mid", "2026-03-01", null, "Unknown", null),
        ]);

        var items = CisaKevProvider.MapCatalog(catalog);

        Assert.Equal(["CVE-2", "CVE-3", "CVE-1"], items.Select(i => i.CveId));
        Assert.False(items[0].KnownRansomware);   // "Unknown" => false
    }

    [Fact]
    public void MapCatalog_defaults_nulls_and_skips_entries_without_cve()
    {
        var catalog = new Catalog(
        [
            new Vuln(null, "V", "P", "No CVE", "2026-01-01", null, "Known", null),     // skipped — no cveID
            new Vuln("", "V", "P", "Empty CVE", "2026-01-01", null, "Known", null),    // skipped — empty cveID
            new Vuln("CVE-9", null, null, null, "not-a-date", null, null, null),       // defaults applied
        ]);

        var v = Assert.Single(CisaKevProvider.MapCatalog(catalog));
        Assert.Equal("CVE-9", v.CveId);
        Assert.Equal("", v.VendorProject);
        Assert.Equal("", v.Product);
        Assert.Equal("", v.Name);
        Assert.Null(v.DateAdded);        // unparseable date => null
        Assert.Null(v.ShortDescription);
        Assert.False(v.KnownRansomware);
        Assert.Null(v.DueDate);
    }

    [Fact]
    public void MapCatalog_handles_null_and_empty()
    {
        Assert.Empty(CisaKevProvider.MapCatalog(null));
        Assert.Empty(CisaKevProvider.MapCatalog(new Catalog(null)));
        Assert.Empty(CisaKevProvider.MapCatalog(new Catalog([])));
    }
}
