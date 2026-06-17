using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Identity;

namespace WorldMonitor.Data.Configurations;

public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.UserId);
        b.Property(u => u.UserId).HasMaxLength(128);
        b.Property(u => u.Email).HasMaxLength(320);
        b.Property(u => u.NormalizedEmail).HasMaxLength(320);
        b.Property(u => u.LocaleTag).HasMaxLength(35);
        b.Property(u => u.LocalePrimary).HasMaxLength(16);
        b.Property(u => u.Timezone).HasMaxLength(64);
        b.Property(u => u.Country).HasMaxLength(2);
        b.HasIndex(u => u.NormalizedEmail).HasDatabaseName("IX_Users_NormalizedEmail");
    }
}
