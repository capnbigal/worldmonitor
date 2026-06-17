using Microsoft.EntityFrameworkCore;
using WorldMonitor.Caching;
using WorldMonitor.Contracts.Json;
using WorldMonitor.Contracts.AirQuality;
using WorldMonitor.Contracts.Displacement;
using WorldMonitor.Contracts.Intel;
using WorldMonitor.Contracts.Market;
using WorldMonitor.Contracts.Natural;
using WorldMonitor.Contracts.News;
using WorldMonitor.Contracts.Seismology;
using WorldMonitor.Contracts.Security;
using WorldMonitor.Contracts.Sentiment;
using WorldMonitor.Contracts.Space;
using WorldMonitor.Contracts.Status;
using WorldMonitor.Contracts.Weather;
using WorldMonitor.Data;
using WorldMonitor.Data.Caching;
using WorldMonitor.Data.Time;
using WorldMonitor.Providers;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitor;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

// Data + cache stack (the layer built in P1a/P1b/P1c).
builder.Services.AddDbContext<WorldMonitorDbContext>(o => o.UseSqlServer(connectionString));
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddScoped<ICacheStore, SqlServerCacheStore>();
// NOTE (slice simplification): WorldMonitorCache is scoped here, so its in-process coalescing + outage
// fallback are per-request only (the durable SQL CacheEntries still gives cross-request caching). A later
// phase should register it as a singleton backed by an IDbContextFactory for process-wide coalescing.
builder.Services.AddScoped<IWorldMonitorCache, WorldMonitorCache>();
builder.Services.AddProblemDetails();

// Upstream providers (no API keys needed) — each registered behind its interface so it's swappable in tests.
// A descriptive User-Agent is required: CoinGecko's Cloudflare front returns 403 for requests with no UA,
// and it's good upstream etiquette for the others too.
const string userAgent = "WorldMonitor/1.0 (+https://github.com/worldmonitor/worldmonitor)";
builder.Services.AddHttpClient<IEarthquakeProvider, UsgsEarthquakeProvider>(c =>
{
    c.BaseAddress = new Uri("https://earthquake.usgs.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<IMarketProvider, CoinGeckoProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.coingecko.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<INaturalEventProvider, EonetProvider>(c =>
{
    c.BaseAddress = new Uri("https://eonet.gsfc.nasa.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
// News fetches absolute feed URLs across many hosts, so no BaseAddress.
builder.Services.AddHttpClient<INewsProvider, RssNewsProvider>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
builder.Services.AddHttpClient<ISecurityAdvisoryProvider, CisaKevProvider>(c =>
{
    c.BaseAddress = new Uri("https://www.cisa.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<IMarketSentimentProvider, FearGreedProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.alternative.me/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<IIntelProvider, GdeltIntelProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.gdeltproject.org/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<ISpaceWeatherProvider, NoaaSpaceWeatherProvider>(c =>
{
    c.BaseAddress = new Uri("https://services.swpc.noaa.gov/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<IWeatherProvider, OpenMeteoWeatherProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.open-meteo.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
builder.Services.AddHttpClient<IAirQualityProvider, OpenMeteoAirQualityProvider>(c =>
{
    c.BaseAddress = new Uri("https://air-quality-api.open-meteo.com/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});
// Service status fetches absolute status-page URLs across many hosts, so no BaseAddress.
builder.Services.AddHttpClient<IServiceStatusProvider, StatusPageProvider>(c => c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent));
builder.Services.AddHttpClient<IDisplacementProvider, UnhcrDisplacementProvider>(c =>
{
    c.BaseAddress = new Uri("https://api.unhcr.org/");
    c.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
});

var app = builder.Build();

app.UseExceptionHandler(); // shape unhandled errors (e.g. USGS upstream down) as RFC 7807 problem responses

// Dev-slice convenience: ensure the schema exists on startup. NOTE: a multi-instance / production
// deployment should apply migrations as a separate step (CI or an init container), not on every app boot.
using (var scope = app.Services.CreateScope())
    await scope.ServiceProvider.GetRequiredService<WorldMonitorDbContext>().Database.MigrateAsync();

app.UseBlazorFrameworkFiles();
app.UseStaticFiles();

// The golden vertical slice: USGS -> provider -> WorldMonitorCache -> SQL CacheEntries -> API.
app.MapGet("/api/seismology/v1/list-earthquakes", async (
    IWorldMonitorCache cache, IEarthquakeProvider usgs, double? min_magnitude) =>
{
    var data = await cache.GetOrSetAsync(
        "seismology:earthquakes:all_day:v1",
        TimeSpan.FromMinutes(5),
        async ct => new ListEarthquakesResponse { Earthquakes = await usgs.FetchAsync(ct) });

    IReadOnlyList<Earthquake> quakes = data?.Earthquakes ?? [];
    if (min_magnitude is { } min)
        quakes = quakes.Where(e => e.Magnitude >= min).ToList();

    return Results.Json(new ListEarthquakesResponse { Earthquakes = quakes }, WmJson.Options);
});

// Markets — top crypto by market cap (CoinGecko), cached 5 min.
app.MapGet("/api/market/v1/top-coins", async (IWorldMonitorCache cache, IMarketProvider market) =>
{
    var data = await cache.GetOrSetAsync(
        "market:top-coins:v1",
        TimeSpan.FromMinutes(5),
        async ct => new ListCoinsResponse { Coins = await market.FetchTopCoinsAsync(25, ct) });
    return Results.Json(data ?? new ListCoinsResponse(), WmJson.Options);
});

// Natural events — open events from NASA EONET, cached 15 min.
app.MapGet("/api/natural/v1/events", async (IWorldMonitorCache cache, INaturalEventProvider natural) =>
{
    var data = await cache.GetOrSetAsync(
        "natural:events:v1",
        TimeSpan.FromMinutes(15),
        async ct => new ListNaturalEventsResponse { Events = await natural.FetchAsync(40, ct) });
    return Results.Json(data ?? new ListNaturalEventsResponse(), WmJson.Options);
});

// World news — aggregated public RSS feeds, cached 5 min.
app.MapGet("/api/news/v1/headlines", async (IWorldMonitorCache cache, INewsProvider news) =>
{
    var data = await cache.GetOrSetAsync(
        "news:headlines:v1",
        TimeSpan.FromMinutes(5),
        async ct => new ListNewsResponse { Items = await news.FetchHeadlinesAsync(60, ct) });
    return Results.Json(data ?? new ListNewsResponse(), WmJson.Options);
});

// Security advisories — CISA Known Exploited Vulnerabilities catalog, cached 6 hours.
app.MapGet("/api/security/v1/known-exploited-vulnerabilities", async (IWorldMonitorCache cache, ISecurityAdvisoryProvider security) =>
{
    var data = await cache.GetOrSetAsync(
        "security:kev:v1",
        TimeSpan.FromHours(6),
        async ct => new ListVulnerabilitiesResponse { Items = await security.FetchAsync(40, ct) });
    return Results.Json(data ?? new ListVulnerabilitiesResponse(), WmJson.Options);
});

// Crypto Fear & Greed index — alternative.me, cached 30 min.
app.MapGet("/api/sentiment/v1/fear-greed", async (IWorldMonitorCache cache, IMarketSentimentProvider sentiment) =>
{
    var data = await cache.GetOrSetAsync(
        "sentiment:fng:v1",
        TimeSpan.FromMinutes(30),
        async ct => new ListFearGreedResponse { Items = await sentiment.FetchAsync(30, ct) });
    return Results.Json(data ?? new ListFearGreedResponse(), WmJson.Options);
});

// Global intel — worldwide news coverage from GDELT, cached 15 min.
app.MapGet("/api/intel/v1/articles", async (IWorldMonitorCache cache, IIntelProvider intel) =>
{
    var data = await cache.GetOrSetAsync(
        "intel:gdelt:v1",
        TimeSpan.FromMinutes(15),
        async ct => new ListIntelResponse { Items = await intel.FetchAsync(40, ct) });
    return Results.Json(data ?? new ListIntelResponse(), WmJson.Options);
});

// Space weather — NOAA planetary K-index (geomagnetic activity), cached 30 min.
app.MapGet("/api/space/v1/kp-index", async (IWorldMonitorCache cache, ISpaceWeatherProvider space) =>
{
    var data = await cache.GetOrSetAsync(
        "space:kp:v1",
        TimeSpan.FromMinutes(30),
        async ct => new ListKpResponse { Items = await space.FetchAsync(24, ct) });
    return Results.Json(data ?? new ListKpResponse(), WmJson.Options);
});

// Global weather — current conditions for major cities (Open-Meteo), cached 15 min.
app.MapGet("/api/weather/v1/cities", async (IWorldMonitorCache cache, IWeatherProvider weather) =>
{
    var data = await cache.GetOrSetAsync(
        "weather:cities:v1",
        TimeSpan.FromMinutes(15),
        async ct => new ListWeatherResponse { Items = await weather.FetchAsync(14, ct) });
    return Results.Json(data ?? new ListWeatherResponse(), WmJson.Options);
});

// Air quality — European AQI & particulates for major cities (Open-Meteo), cached 30 min.
app.MapGet("/api/air-quality/v1/cities", async (IWorldMonitorCache cache, IAirQualityProvider air) =>
{
    var data = await cache.GetOrSetAsync(
        "airquality:cities:v1",
        TimeSpan.FromMinutes(30),
        async ct => new ListAirQualityResponse { Items = await air.FetchAsync(14, ct) });
    return Results.Json(data ?? new ListAirQualityResponse(), WmJson.Options);
});

// Service status — statuspage.io summaries for major cloud/internet services, cached 5 min.
app.MapGet("/api/status/v1/services", async (IWorldMonitorCache cache, IServiceStatusProvider status) =>
{
    var data = await cache.GetOrSetAsync(
        "status:services:v1",
        TimeSpan.FromMinutes(5),
        async ct => new ListServiceStatusResponse { Items = await status.FetchAsync(12, ct) });
    return Results.Json(data ?? new ListServiceStatusResponse(), WmJson.Options);
});

// Forced displacement — refugees/asylum-seekers/IDPs by country of origin (UNHCR), cached 24 hours.
app.MapGet("/api/displacement/v1/by-country", async (IWorldMonitorCache cache, IDisplacementProvider displacement) =>
{
    var data = await cache.GetOrSetAsync(
        "displacement:country:v1",
        TimeSpan.FromHours(24),
        async ct => new ListDisplacementResponse { Items = await displacement.FetchAsync(40, ct) });
    return Results.Json(data ?? new ListDisplacementResponse(), WmJson.Options);
});

app.MapFallbackToFile("index.html");

app.Run();

// Exposed so a WebApplicationFactory-based integration test can boot the host.
public partial class Program;
