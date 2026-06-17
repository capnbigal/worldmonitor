using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities;
using WorldMonitor.Data.Entities.Identity;
using WorldMonitor.Data.Entities.Notifications;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data;

public class WorldMonitorDbContext(DbContextOptions<WorldMonitorDbContext> options) : DbContext(options)
{
    public DbSet<CacheEntry> CacheEntries => Set<CacheEntry>();
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<FollowedCountry> FollowedCountries => Set<FollowedCountry>();
    public DbSet<NotificationChannel> NotificationChannels => Set<NotificationChannel>();
    public DbSet<AlertRule> AlertRules => Set<AlertRule>();
    public DbSet<TelegramPairingToken> TelegramPairingTokens => Set<TelegramPairingToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(WorldMonitorDbContext).Assembly);

        // Web-push endpoint is globally unique (one browser endpoint ↦ one user) — the hard backstop
        // for the cross-user ownership guard. Filtered so non-web-push rows (Endpoint NULL) are excluded.
        modelBuilder.Entity<WebPushChannel>()
            .HasIndex(w => w.Endpoint)
            .IsUnique()
            .HasFilter("[Endpoint] IS NOT NULL")
            .HasDatabaseName("UX_NotificationChannels_Endpoint");

        // Slack, Discord, and Webhook all declare WebhookEnvelope — map them to one shared column.
        modelBuilder.Entity<SlackChannel>().Property(s => s.WebhookEnvelope).HasColumnName("WebhookEnvelope");
        modelBuilder.Entity<DiscordChannel>().Property(d => d.WebhookEnvelope).HasColumnName("WebhookEnvelope");
        modelBuilder.Entity<WebhookChannel>().Property(w => w.WebhookEnvelope).HasColumnName("WebhookEnvelope");
    }
}
