using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Notifications;

namespace WorldMonitor.Data.Configurations;

public sealed class TelegramPairingTokenConfiguration : IEntityTypeConfiguration<TelegramPairingToken>
{
    public void Configure(EntityTypeBuilder<TelegramPairingToken> b)
    {
        b.ToTable("TelegramPairingTokens");
        b.HasKey(t => t.Id);
        b.Property(t => t.UserId).HasMaxLength(128);
        b.Property(t => t.Token).HasMaxLength(64);
        b.Property(t => t.Variant).HasMaxLength(64);
        b.HasIndex(t => t.Token).IsUnique().HasDatabaseName("UX_TelegramPairingTokens_Token");
        b.HasIndex(t => t.UserId).HasDatabaseName("IX_TelegramPairingTokens_User");
    }
}
