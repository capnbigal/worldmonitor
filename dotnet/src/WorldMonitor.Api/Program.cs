using Microsoft.EntityFrameworkCore;
using WorldMonitor.Caching;
using WorldMonitor.Contracts.Json;
using WorldMonitor.Contracts.Market;
using WorldMonitor.Contracts.Natural;
using WorldMonitor.Contracts.Seismology;
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

app.MapFallbackToFile("index.html");

app.Run();

// Exposed so a WebApplicationFactory-based integration test can boot the host.
public partial class Program;
