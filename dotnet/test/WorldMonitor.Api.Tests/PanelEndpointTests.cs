using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldMonitor.Contracts.Intel;
using WorldMonitor.Contracts.Market;
using WorldMonitor.Contracts.Natural;
using WorldMonitor.Contracts.News;
using WorldMonitor.Contracts.Security;
using WorldMonitor.Contracts.Sentiment;
using WorldMonitor.Contracts.Space;
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

public sealed class FakeSecurityAdvisoryProvider : ISecurityAdvisoryProvider
{
    public Task<IReadOnlyList<KnownVulnerability>> FetchAsync(int count = 40, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<KnownVulnerability>>(
        [
            new KnownVulnerability { CveId = "CVE-2026-1", VendorProject = "Acme", Product = "Widget", Name = "Acme Widget RCE", KnownRansomware = true },
        ]);
}

public sealed class FakeMarketSentimentProvider : IMarketSentimentProvider
{
    public Task<IReadOnlyList<FearGreedReading>> FetchAsync(int count = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FearGreedReading>>(
        [
            new FearGreedReading { Value = 22, Classification = "Extreme Fear", At = 1_781_654_400_000 },
        ]);
}

public sealed class FakeIntelProvider : IIntelProvider
{
    public Task<IReadOnlyList<IntelArticle>> FetchAsync(int count = 40, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<IntelArticle>>(
        [
            new IntelArticle { Title = "Global headline", Url = "https://example.com/x", Domain = "example.com", SourceCountry = "United States", SeenAt = 1_781_678_400_000 },
        ]);
}

public sealed class FakeSpaceWeatherProvider : ISpaceWeatherProvider
{
    public Task<IReadOnlyList<KpReading>> FetchAsync(int count = 24, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<KpReading>>(
        [
            new KpReading { At = 1_781_654_400_000, Kp = 5.0 },
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
            services.RemoveAll<ISecurityAdvisoryProvider>();             // drop the real CISA HttpClient
            services.AddSingleton<ISecurityAdvisoryProvider, FakeSecurityAdvisoryProvider>();
            services.RemoveAll<IMarketSentimentProvider>();             // drop the real alternative.me HttpClient
            services.AddSingleton<IMarketSentimentProvider, FakeMarketSentimentProvider>();
            services.RemoveAll<IIntelProvider>();                        // drop the real GDELT HttpClient
            services.AddSingleton<IIntelProvider, FakeIntelProvider>();
            services.RemoveAll<ISpaceWeatherProvider>();                 // drop the real NOAA HttpClient
            services.AddSingleton<ISpaceWeatherProvider, FakeSpaceWeatherProvider>();
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

    [Fact]
    public async Task Security_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListVulnerabilitiesResponse>("api/security/v1/known-exploited-vulnerabilities");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("CVE-2026-1", item.CveId);
        Assert.True(item.KnownRansomware);
    }

    [Fact]
    public async Task Fear_greed_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListFearGreedResponse>("api/sentiment/v1/fear-greed");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal(22, item.Value);
        Assert.Equal("Extreme Fear", item.Classification);
    }

    [Fact]
    public async Task Intel_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListIntelResponse>("api/intel/v1/articles");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("Global headline", item.Title);
        Assert.Equal("United States", item.SourceCountry);
    }

    [Fact]
    public async Task Space_weather_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListKpResponse>("api/space/v1/kp-index");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal(5.0, item.Kp);
    }
}
