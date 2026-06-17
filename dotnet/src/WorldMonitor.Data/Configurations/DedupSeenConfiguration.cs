using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class DedupSeenConfiguration : IEntityTypeConfiguration<DedupSeen>
{
    public void Configure(EntityTypeBuilder<DedupSeen> b)
    {
        b.ToTable("DedupSeen");
        b.HasKey(d => d.DedupKey);
        b.Property(d => d.DedupKey).HasMaxLength(256);
        b.Property(d => d.SignalType).HasMaxLength(64);
        b.HasIndex(d => d.SeenAt).HasDatabaseName("IX_DedupSeen_SeenAt");
    }
}
