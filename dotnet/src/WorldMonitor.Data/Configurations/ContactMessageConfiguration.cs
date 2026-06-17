using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Waitlist;

namespace WorldMonitor.Data.Configurations;

public sealed class ContactMessageConfiguration : IEntityTypeConfiguration<ContactMessage>
{
    public void Configure(EntityTypeBuilder<ContactMessage> b)
    {
        b.ToTable("ContactMessages");
        b.HasKey(m => m.Id);
        b.Property(m => m.Name).HasMaxLength(200);
        b.Property(m => m.Email).HasMaxLength(320);
        b.Property(m => m.Organization).HasMaxLength(200);
        b.Property(m => m.Phone).HasMaxLength(64);
        b.Property(m => m.Source).HasMaxLength(64);
        b.Property(m => m.NormalizedEmail).HasMaxLength(320);
        b.HasIndex(m => new { m.NormalizedEmail, m.ReceivedAt }).HasDatabaseName("IX_ContactMessages_Email_Received");
    }
}
