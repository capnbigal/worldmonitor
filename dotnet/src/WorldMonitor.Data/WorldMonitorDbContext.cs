using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities;

namespace WorldMonitor.Data;

public class WorldMonitorDbContext(DbContextOptions<WorldMonitorDbContext> options) : DbContext(options)
{
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorldMonitorDbContext).Assembly);
    }
}
