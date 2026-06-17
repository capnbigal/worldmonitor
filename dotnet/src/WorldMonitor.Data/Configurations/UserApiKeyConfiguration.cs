using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Access;

namespace WorldMonitor.Data.Configurations;

public sealed class UserApiKeyConfiguration : IEntityTypeConfiguration<UserApiKey>
{
    public void Configure(EntityTypeBuilder<UserApiKey> b)
    {
        b.ToTable("UserApiKeys");
        b.HasKey(k => k.Id);
        b.Property(k => k.UserId).HasMaxLength(128);
        b.Property(k => k.Name).HasMaxLength(128);
        b.Property(k => k.KeyPrefix).HasMaxLength(16);
        b.Property(k => k.KeyHash).HasMaxLength(64);
        b.HasIndex(k => k.KeyHash).IsUnique().HasDatabaseName("UX_UserApiKeys_KeyHash");
        b.HasIndex(k => k.UserId).HasDatabaseName("IX_UserApiKeys_User");
    }
}
