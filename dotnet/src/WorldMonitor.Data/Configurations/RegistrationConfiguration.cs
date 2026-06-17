using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class RegistrationConfiguration : IEntityTypeConfiguration<Registration>
{
    public void Configure(EntityTypeBuilder<Registration> b)
    {
        b.ToTable("Registrations");
        b.HasKey(r => r.Id);
        b.Property(r => r.Email).HasMaxLength(320);
        b.Property(r => r.NormalizedEmail).HasMaxLength(320);
        b.Property(r => r.Source).HasMaxLength(64);
        b.Property(r => r.AppVersion).HasMaxLength(32);
        b.Property(r => r.ReferralCode).HasMaxLength(64);
        b.Property(r => r.ReferredBy).HasMaxLength(128);
        b.HasIndex(r => r.NormalizedEmail).IsUnique().HasDatabaseName("UX_Registrations_NormalizedEmail");
        b.HasIndex(r => r.ReferralCode).HasDatabaseName("IX_Registrations_ReferralCode");
    }
}
