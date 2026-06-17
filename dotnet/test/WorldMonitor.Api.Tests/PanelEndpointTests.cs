using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldMonitor.Contracts.Market;
using WorldMonitor.Contracts.Natural;
using WorldMonitor.Contracts.News;
using WorldMonitor.Data;
using WorldMonitor.Providers;
using Xunit;

namespace WorldMonitor.Api.Tests;

public sealed class FakeMarketProvider : IMarketProvider
{
    public Task<IReadOnlyList<CoinQuote>> FetchTopCoinsAsync(int count = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CoinQuote>>(
        [
            new CoinQuote { Id = "bitcoin", Symbol = "BTC", Name = "Bitcoin", Price = 65000, ChangePercent24h = 1.5, MarketCap = 1_280_000_000_000 },
        ]);
}

public sealed class FakeNaturalEventProvider : INaturalEventProvider
{
    public Task<IReadOnlyList<NaturalEvent>> FetchAsync(int count = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NaturalEvent>>(
        [
            new NaturalEvent { Id = "EONET_1", Title = "Wildfire A", Category = "Wildfires", Latitude = 38.1, Longitude = -120.5 },
        ]);
}

public sealed class FakeNewsProvider : INewsProvider
{
    public Task<IReadOnlyList<NewsItem>> FetchHeadlinesAsync(int count = 60, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<NewsItem>>(
        [
            new NewsItem { Id = "n1", Title = "Headline One", Link = "https://example.com/a", Source = "BBC World", PublishedAt = 1_750_000_000_000 },
        ]);
}

public sealed class PanelApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            services.UseTestDatabase();                                   // isolate from the dev DB
            services.RemoveAll<IMarketProvider>();                        // drop the real CoinGecko HttpClient
            services.AddSingleton<IMarketProvider, FakeMarketProvider>();
            services.RemoveAll<INaturalEventProvider>();                 // drop the real EONET HttpClient
            services.AddSingleton<INaturalEventProvider, FakeNaturalEventProvider>();
            services.RemoveAll<INewsProvider>();                         // drop the real RSS HttpClient
            services.AddSingleton<INewsProvider, FakeNewsProvider>();
        });
    }

    /// <summary>Start the cache cold so a test is deterministic regardless of prior runs / the TTL.</summary>
    public async Task ResetCacheAsync()
    {
        using var scope = Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<WorldMonitorDbContext>().CacheEntries.ExecuteDeleteAsync();
    }
}

[Trait("Category", "Integration")]
[Collection(ApiIntegrationCollection.Name)]
public sealed class PanelEndpointTests(PanelApiFactory factory) : IClassFixture<PanelApiFactory>
{
    [Fact]
    public async Task Top_coins_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListCoinsResponse>("api/market/v1/top-coins");

        Assert.NotNull(resp);
        var coin = Assert.Single(resp!.Coins);
        Assert.Equal("BTC", coin.Symbol);
        Assert.Equal(65000, coin.Price);
    }

    [Fact]
    public async Task Natural_events_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListNaturalEventsResponse>("api/natural/v1/events");

        Assert.NotNull(resp);
        var ev = Assert.Single(resp!.Events);
        Assert.Equal("EONET_1", ev.Id);
        Assert.Equal("Wildfires", ev.Category);
    }

    [Fact]
    public async Task News_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListNewsResponse>("api/news/v1/headlines");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("Headline One", item.Title);
        Assert.Equal("BBC World", item.Source);
    }
}
