using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class UserReferralCreditConfiguration : IEntityTypeConfiguration<UserReferralCredit>
{
    public void Configure(EntityTypeBuilder<UserReferralCredit> b)
    {
        b.ToTable("UserReferralCredits");
        b.HasKey(c => c.Id);
        b.Property(c => c.ReferrerUserId).HasMaxLength(128);
        b.Property(c => c.RefereeEmail).HasMaxLength(320);
        b.HasIndex(c => new { c.ReferrerUserId, c.RefereeEmail }).IsUnique().HasDatabaseName("UX_UserReferralCredits_Referrer_Email");
    }
}
