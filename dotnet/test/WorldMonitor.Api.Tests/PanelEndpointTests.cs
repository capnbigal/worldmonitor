using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldMonitor.Contracts.AirQuality;
using WorldMonitor.Contracts.Displacement;
using WorldMonitor.Contracts.Economy;
using WorldMonitor.Contracts.Fx;
using WorldMonitor.Contracts.Intel;
using WorldMonitor.Contracts.Market;
using WorldMonitor.Contracts.Natural;
using WorldMonitor.Contracts.News;
using WorldMonitor.Contracts.Security;
using WorldMonitor.Contracts.Sentiment;
using WorldMonitor.Contracts.Space;
using WorldMonitor.Contracts.Status;
using WorldMonitor.Contracts.Tech;
using WorldMonitor.Contracts.Trending;
using WorldMonitor.Contracts.Weather;
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

public sealed class FakeWeatherProvider : IWeatherProvider
{
    public Task<IReadOnlyList<CityWeather>> FetchAsync(int count = 14, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CityWeather>>(
        [
            new CityWeather { City = "London", TemperatureC = 23.9, WindKph = 18.7, Condition = "Partly cloudy" },
        ]);
}

public sealed class FakeAirQualityProvider : IAirQualityProvider
{
    public Task<IReadOnlyList<CityAirQuality>> FetchAsync(int count = 14, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<CityAirQuality>>(
        [
            new CityAirQuality { City = "London", Aqi = 20, Pm25 = 4.2, Pm10 = 6.5 },
        ]);
}

public sealed class FakeServiceStatusProvider : IServiceStatusProvider
{
    public Task<IReadOnlyList<ServiceStatus>> FetchAsync(int count = 12, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ServiceStatus>>(
        [
            new ServiceStatus { Service = "GitHub", Indicator = "none", Description = "All Systems Operational" },
        ]);
}

public sealed class FakeDisplacementProvider : IDisplacementProvider
{
    public Task<IReadOnlyList<DisplacementByCountry>> FetchAsync(int count = 40, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<DisplacementByCountry>>(
        [
            new DisplacementByCountry { Country = "Afghanistan", Refugees = 5_766_586, AsylumSeekers = 384_732, Idps = 3_199_710 },
        ]);
}

public sealed class FakeFxRateProvider : IFxRateProvider
{
    public Task<IReadOnlyList<FxRate>> FetchAsync(int count = 40, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<FxRate>>(
        [
            new FxRate { Currency = "EUR", Rate = 0.86252 },
        ]);
}

public sealed class FakeHackerNewsProvider : IHackerNewsProvider
{
    public Task<IReadOnlyList<HackerNewsStory>> FetchAsync(int count = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<HackerNewsStory>>(
        [
            new HackerNewsStory { Id = 42_000_000, Title = "Show HN: World Monitor", Url = "https://example.com/x", Score = 256, By = "dev", Comments = 42, At = 1_781_678_400_000 },
        ]);
}

public sealed class FakeWikipediaTrendingProvider : IWikipediaTrendingProvider
{
    public Task<IReadOnlyList<TrendingArticle>> FetchAsync(int count = 30, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<TrendingArticle>>(
        [
            new TrendingArticle { Title = "Oliver Tree", Views = 1_495_154, Description = "Musician", Url = "https://en.wikipedia.org/wiki/Oliver_Tree" },
        ]);
}

public sealed class FakeEconomyProvider : IEconomyProvider
{
    public Task<IReadOnlyList<EconomyIndicator>> FetchAsync(int count = 20, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<EconomyIndicator>>(
        [
            new EconomyIndicator { Country = "India", GrowthPercent = 7.0, Year = "2024" },
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
            services.RemoveAll<IWeatherProvider>();                      // drop the real Open-Meteo HttpClient
            services.AddSingleton<IWeatherProvider, FakeWeatherProvider>();
            services.RemoveAll<IAirQualityProvider>();                   // drop the real Open-Meteo air-quality HttpClient
            services.AddSingleton<IAirQualityProvider, FakeAirQualityProvider>();
            services.RemoveAll<IServiceStatusProvider>();                // drop the real statuspage.io HttpClient
            services.AddSingleton<IServiceStatusProvider, FakeServiceStatusProvider>();
            services.RemoveAll<IDisplacementProvider>();                 // drop the real UNHCR HttpClient
            services.AddSingleton<IDisplacementProvider, FakeDisplacementProvider>();
            services.RemoveAll<IFxRateProvider>();                       // drop the real Frankfurter HttpClient
            services.AddSingleton<IFxRateProvider, FakeFxRateProvider>();
            services.RemoveAll<IHackerNewsProvider>();                   // drop the real Hacker News HttpClient
            services.AddSingleton<IHackerNewsProvider, FakeHackerNewsProvider>();
            services.RemoveAll<IWikipediaTrendingProvider>();            // drop the real Wikipedia HttpClient
            services.AddSingleton<IWikipediaTrendingProvider, FakeWikipediaTrendingProvider>();
            services.RemoveAll<IEconomyProvider>();                      // drop the real World Bank HttpClient
            services.AddSingleton<IEconomyProvider, FakeEconomyProvider>();
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

    [Fact]
    public async Task Weather_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListWeatherResponse>("api/weather/v1/cities");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("London", item.City);
        Assert.Equal("Partly cloudy", item.Condition);
    }

    [Fact]
    public async Task Air_quality_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListAirQualityResponse>("api/air-quality/v1/cities");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("London", item.City);
        Assert.Equal(20, item.Aqi);
    }

    [Fact]
    public async Task Service_status_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListServiceStatusResponse>("api/status/v1/services");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("GitHub", item.Service);
        Assert.Equal("none", item.Indicator);
    }

    [Fact]
    public async Task Displacement_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListDisplacementResponse>("api/displacement/v1/by-country");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("Afghanistan", item.Country);
        Assert.Equal(5_766_586, item.Refugees);
    }

    [Fact]
    public async Task Fx_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListFxRatesResponse>("api/fx/v1/rates");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("EUR", item.Currency);
        Assert.Equal(0.86252, item.Rate);
    }

    [Fact]
    public async Task Hacker_news_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListHackerNewsResponse>("api/tech/v1/hacker-news");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("Show HN: World Monitor", item.Title);
        Assert.Equal(256, item.Score);
    }

    [Fact]
    public async Task Trending_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListTrendingResponse>("api/trending/v1/wikipedia");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("Oliver Tree", item.Title);
        Assert.Equal(1_495_154, item.Views);
    }

    [Fact]
    public async Task Economy_endpoint_returns_provider_data_through_the_cache()
    {
        await factory.ResetCacheAsync();
        var client = factory.CreateClient();
        var resp = await client.GetFromJsonAsync<ListEconomyResponse>("api/economy/v1/gdp-growth");

        Assert.NotNull(resp);
        var item = Assert.Single(resp!.Items);
        Assert.Equal("India", item.Country);
        Assert.Equal(7.0, item.GrowthPercent);
    }
}
