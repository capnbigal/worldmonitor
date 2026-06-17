using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Identity;

namespace WorldMonitor.Data.Configurations;

public sealed class UserPreferenceConfiguration : IEntityTypeConfiguration<UserPreference>
{
    public void Configure(EntityTypeBuilder<UserPreference> b)
    {
        b.ToTable("UserPreferences");
        b.HasKey(p => p.Id);
        b.Property(p => p.UserId).HasMaxLength(128);
        b.Property(p => p.Variant).HasMaxLength(64);
        b.Property(p => p.Data).HasColumnType("nvarchar(max)");
        b.HasIndex(p => new { p.UserId, p.Variant }).IsUnique().HasDatabaseName("UX_UserPreferences_User_Variant");
    }
}
