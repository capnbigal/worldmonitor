using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class EmailSuppressionConfiguration : IEntityTypeConfiguration<EmailSuppression>
{
    public void Configure(EntityTypeBuilder<EmailSuppression> b)
    {
        b.ToTable("EmailSuppressions");
        b.HasKey(s => s.Id);
        b.Property(s => s.NormalizedEmail).HasMaxLength(320);
        b.Property(s => s.Reason).HasMaxLength(16);
        b.Property(s => s.Source).HasMaxLength(64);
        b.HasIndex(s => s.NormalizedEmail).IsUnique().HasDatabaseName("UX_EmailSuppressions_NormalizedEmail");
    }
}
