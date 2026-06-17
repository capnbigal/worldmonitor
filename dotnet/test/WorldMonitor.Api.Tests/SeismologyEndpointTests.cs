using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.AspNetCore.Hosting;
using WorldMonitor.Contracts.Seismology;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

/// <summary>Deterministic provider so the endpoint test never depends on the live USGS feed.</summary>
internal sealed class FakeEarthquakeProvider : IEarthquakeProvider
{
    public Task<IReadOnlyList<Earthquake>> FetchAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<Earthquake>>(
        [
            new Earthquake { Id = "fake1", Place = "Testville", Magnitude = 5.5, DepthKm = 10, OccurredAt = 1_700_000_000_000, SourceUrl = "" },
            new Earthquake { Id = "fake2", Place = "Quaketon", Magnitude = 3.1, DepthKm = 5, OccurredAt = 1_700_000_001_000, SourceUrl = "" },
        ]);
}

public sealed class SeismologyApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] =
                @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorApiTest;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False",
        }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEarthquakeProvider>();              // drop the real USGS HttpClient
            services.AddScoped<IEarthquakeProvider, FakeEarthquakeProvider>();
        });
    }
}

[Trait("Category", "Integration")]
public sealed class SeismologyEndpointTests(SeismologyApiFactory factory) : IClassFixture<SeismologyApiFactory>
{
    [Fact]
    public async Task Endpoint_returns_provider_earthquakes_through_the_cache()
    {
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListEarthquakesResponse>("api/seismology/v1/list-earthquakes");

        Assert.NotNull(resp);
        Assert.Equal(2, resp!.Earthquakes.Count);
        Assert.Contains(resp.Earthquakes, e => e.Id == "fake1");
        Assert.Contains(resp.Earthquakes, e => e.Id == "fake2");
    }

    [Fact]
    public async Task Endpoint_filters_by_min_magnitude()
    {
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListEarthquakesResponse>("api/seismology/v1/list-earthquakes?min_magnitude=5");

        Assert.NotNull(resp);
        Assert.All(resp!.Earthquakes, e => Assert.True(e.Magnitude >= 5));
        Assert.Contains(resp.Earthquakes, e => e.Id == "fake1"); // 5.5 kept
        Assert.DoesNotContain(resp.Earthquakes, e => e.Id == "fake2"); // 3.1 filtered out
    }
}
