using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class AlertRuleConfiguration : IEntityTypeConfiguration<AlertRule>
{
    public void Configure(EntityTypeBuilder<AlertRule> b)
    {
        b.ToTable("AlertRules");
        b.HasKey(r => r.Id);
        b.Property(r => r.UserId).HasMaxLength(128);
        b.Property(r => r.Variant).HasMaxLength(64);
        b.Property(r => r.Sensitivity).HasMaxLength(16);
        b.Property(r => r.QuietHoursOverride).HasMaxLength(32);
        b.Property(r => r.DigestMode).HasMaxLength(16);
        b.Property(r => r.QuietHoursTimezone).HasMaxLength(64);
        b.Property(r => r.DigestTimezone).HasMaxLength(64);
        // EventTypes/Channels/Countries map to JSON columns automatically (EF Core primitive collections).
        b.HasIndex(r => new { r.UserId, r.Variant }).IsUnique().HasDatabaseName("UX_AlertRules_User_Variant");
        b.HasIndex(r => r.Enabled).HasDatabaseName("IX_AlertRules_Enabled");
    }
}
