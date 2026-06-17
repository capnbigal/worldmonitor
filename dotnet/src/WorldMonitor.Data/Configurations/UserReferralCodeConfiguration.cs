using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class UserReferralCodeConfiguration : IEntityTypeConfiguration<UserReferralCode>
{
    public void Configure(EntityTypeBuilder<UserReferralCode> b)
    {
        b.ToTable("UserReferralCodes");
        b.HasKey(c => c.Id);
        b.Property(c => c.UserId).HasMaxLength(128);
        b.Property(c => c.Code).HasMaxLength(64);
        b.HasIndex(c => c.Code).IsUnique().HasDatabaseName("UX_UserReferralCodes_Code");
        b.HasIndex(c => c.UserId).IsUnique().HasDatabaseName("UX_UserReferralCodes_User");
    }
}
