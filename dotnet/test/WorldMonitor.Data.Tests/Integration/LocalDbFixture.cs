using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data;
using Xunit;

namespace WorldMonitor.Data.Tests.Integration;

/// <summary>One migrated LocalDB database for the whole integration collection.
/// Tests use unique (GUID) cache keys so they don't collide. Database is dropped on dispose.</summary>
public sealed class LocalDbFixture : IAsyncLifetime
{
    public string ConnectionString { get; } =
        @"Server=(localdb)\MSSQLLocalDB;Database=WorldMonitorCacheTests;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False";

    public WorldMonitorDbContext NewContext()
        => new(new DbContextOptionsBuilder<WorldMonitorDbContext>().UseSqlServer(ConnectionString).Options);

    public async Task InitializeAsync()
    {
        await using var ctx = NewContext();
        await ctx.Database.EnsureDeletedAsync();
        await ctx.Database.MigrateAsync(); // proves migrations apply to a real SQL Server engine
    }

    public async Task DisposeAsync()
    {
        await using var ctx = NewContext();
        await ctx.Database.EnsureDeletedAsync();
    }
}

[CollectionDefinition("LocalDb")]
public sealed class LocalDbCollection : ICollectionFixture<LocalDbFixture>;
