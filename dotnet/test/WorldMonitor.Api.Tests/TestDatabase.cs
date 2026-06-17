using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WorldMonitor.Data;

namespace WorldMonitor.Api.Tests;

internal static class TestDatabase
{
    public const string ConnectionString =
        @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorApiTest;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    /// <summary>Repoints <see cref="WorldMonitorDbContext"/> at an isolated test database. We override the
    /// registered <see cref="DbContextOptions"/> in DI rather than the connection-string config because
    /// Program.cs reads the connection string at builder time — before WebApplicationFactory's
    /// <c>ConfigureAppConfiguration</c> callbacks run — so a config override silently has no effect and the
    /// tests would otherwise write to the real dev database.</summary>
    public static IServiceCollection UseTestDatabase(this IServiceCollection services)
    {
        services.RemoveAll<DbContextOptions<WorldMonitorDbContext>>();
        services.RemoveAll<DbContextOptions>();
        services.AddDbContext<WorldMonitorDbContext>(o => o.UseSqlServer(ConnectionString));
        return services;
    }
}
