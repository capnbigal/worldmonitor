using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities;
using WorldMonitor.Data.Entities.Identity;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data;

public class WorldMonitorDbContext(DbContextOptions<WorldMonitorDbContext> options) : DbContext(options)
{
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<FollowedCountry> FollowedCountries => Set<FollowedCountry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorldMonitorDbContext).Assembly);
    }
}
