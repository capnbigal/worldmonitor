using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class NotificationChannelConfiguration : IEntityTypeConfiguration<NotificationChannel>
{
    public void Configure(EntityTypeBuilder<NotificationChannel> b)
    {
        b.ToTable("NotificationChannels");
        b.HasKey(c => c.Id);
        b.Property(c => c.UserId).HasMaxLength(128);
        b.Property(c => c.ChannelType).HasMaxLength(16);

        b.HasDiscriminator(c => c.ChannelType)
            .HasValue<TelegramChannel>("telegram")
            .HasValue<SlackChannel>("slack")
            .HasValue<EmailChannel>("email")
            .HasValue<DiscordChannel>("discord")
            .HasValue<WebhookChannel>("webhook")
            .HasValue<WebPushChannel>("web_push");

        // One channel per type per user (legacy `.unique()` on by_user_channel).
        b.HasIndex(c => new { c.UserId, c.ChannelType }).IsUnique().HasDatabaseName("UX_NotificationChannels_User_Channel");

        // Note: the filtered UNIQUE(Endpoint) index on WebPushChannel is configured in
        // WorldMonitorDbContext.OnModelCreating via modelBuilder.Entity<WebPushChannel>().HasIndex(...)
        // because EntityTypeBuilder<NotificationChannel> does not expose Entity<TDerived>().
    }
}
