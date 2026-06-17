using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldMonitor.Contracts.Seismology;
using WorldMonitor.Data;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

/// <summary>Deterministic provider (so the endpoint test never depends on the live USGS feed) that
/// counts how many times it was fetched — used to prove the cache read-through.</summary>
public sealed class FakeEarthquakeProvider : IEarthquakeProvider
{
    private int _calls;
    public int CallCount => Volatile.Read(ref _calls);

    public Task<IReadOnlyList<Earthquake>> FetchAsync(CancellationToken ct = default)
    {
        Interlocked.Increment(ref _calls);
        return Task.FromResult<IReadOnlyList<Earthquake>>(
        [
            new Earthquake { Id = "fake1", Place = "Testville", Magnitude = 5.5, DepthKm = 10, OccurredAt = 1_700_000_000_000, SourceUrl = "" },
            new Earthquake { Id = "fake2", Place = "Quaketon", Magnitude = 3.1, DepthKm = 5, OccurredAt = 1_700_000_001_000, SourceUrl = "" },
        ]);
    }
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
            services.RemoveAll<IEarthquakeProvider>();                       // drop the real USGS HttpClient
            services.AddSingleton<IEarthquakeProvider, FakeEarthquakeProvider>(); // singleton ⇒ CallCount aggregates across requests
        });
    }

    public FakeEarthquakeProvider Provider => (FakeEarthquakeProvider)Services.GetRequiredService<IEarthquakeProvider>();

    /// <summary>Start the cache cold so a test is deterministic regardless of prior runs / the 5-min TTL.</summary>
    public async Task ResetCacheAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorldMonitorDbContext>().CacheEntries.ExecuteDeleteAsync();
    }
}

[Trait("Category", "Integration")]
public sealed class SeismologyEndpointTests(SeismologyApiFactory factory) : IClassFixture<SeismologyApiFactory>
{
    [Fact]
    public async Task Endpoint_returns_provider_earthquakes_through_the_cache()
    {
        await factory.ResetCacheAsync();
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
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListEarthquakesResponse>("api/seismology/v1/list-earthquakes?min_magnitude=5");

        Assert.NotNull(resp);
        Assert.All(resp!.Earthquakes, e => Assert.True(e.Magnitude >= 5));
        Assert.Contains(resp.Earthquakes, e => e.Id == "fake1");      // 5.5 kept
        Assert.DoesNotContain(resp.Earthquakes, e => e.Id == "fake2"); // 3.1 filtered out
    }

    [Fact]
    public async Task Second_request_is_served_from_cache_without_refetching_upstream()
    {
        await factory.ResetCacheAsync();
        var before = factory.Provider.CallCount;
        var client = factory.CreateClient();

        await client.GetFromJsonAsync<ListEarthquakesResponse>("api/seismology/v1/list-earthquakes"); // miss ⇒ fetch
        await client.GetFromJsonAsync<ListEarthquakesResponse>("api/seismology/v1/list-earthquakes"); // hit ⇒ no fetch

        Assert.Equal(before + 1, factory.Provider.CallCount); // upstream fetched exactly once; 2nd served from SQL cache
    }
}
