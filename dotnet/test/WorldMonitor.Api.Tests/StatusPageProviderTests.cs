using System.Text.Json;
using WorldMonitor.Providers;
using Xunit;
using Summary = WorldMonitor.Providers.StatusPageProvider.Summary;

namespace WorldMonitor.Api.Tests;

public class StatusPageProviderTests
{
    [Fact]
    public void Summary_binds_statuspage_v2_fields()
    {
        // Shape returned by /api/v2/status.json.
        const string json = """
        {"page":{"name":"GitHub"},"status":{"indicator":"none","description":"All Systems Operational"}}
        """;

        var summary = JsonSerializer.Deserialize<Summary>(json);
        var status = StatusPageProvider.MapStatus("GitHub", summary);

        Assert.Equal("GitHub", status.Service);
        Assert.Equal("none", status.Indicator);
        Assert.Equal("All Systems Operational", status.Description);
    }

    [Fact]
    public void MapStatus_maps_service_and_status_fields()
    {
        var summary = new Summary(new StatusPageProvider.StatusDto("major", "Partial Outage"));

        var status = StatusPageProvider.MapStatus("Cloudflare", summary);

        Assert.Equal("Cloudflare", status.Service);
        Assert.Equal("major", status.Indicator);
        Assert.Equal("Partial Outage", status.Description);
    }

    [Fact]
    public void MapStatus_defaults_null_summary_and_status()
    {
        var fromNullSummary = StatusPageProvider.MapStatus("Discord", null);
        Assert.Equal("Discord", fromNullSummary.Service);
        Assert.Equal("unknown", fromNullSummary.Indicator);
        Assert.Equal("", fromNullSummary.Description);

        var fromNullStatus = StatusPageProvider.MapStatus("OpenAI", new Summary(null));
        Assert.Equal("unknown", fromNullStatus.Indicator);
        Assert.Equal("", fromNullStatus.Description);

        var fromNullFields = StatusPageProvider.MapStatus("Zoom", new Summary(new StatusPageProvider.StatusDto(null, null)));
        Assert.Equal("unknown", fromNullFields.Indicator);
        Assert.Equal("", fromNullFields.Description);
    }
}
