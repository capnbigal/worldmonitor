using Microsoft.EntityFrameworkCore;
using WorldMonitor.Caching;
using WorldMonitor.Contracts.Json;
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
builder.Services.AddScoped<IWorldMonitorCache, WorldMonitorCache>();

// Upstream provider (no API key needed) — registered behind IEarthquakeProvider so it's swappable in tests.
builder.Services.AddHttpClient<IEarthquakeProvider, UsgsEarthquakeProvider>(c => c.BaseAddress = new Uri("https://earthquake.usgs.gov/"));

var app = builder.Build();

// Dev convenience: ensure the schema exists on startup.
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

app.MapFallbackToFile("index.html");

app.Run();

// Exposed so a WebApplicationFactory-based integration test can boot the host.
public partial class Program;
