using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities;

namespace WorldMonitor.Data.Configurations;

public sealed class CacheEntryConfiguration : IEntityTypeConfiguration<CacheEntry>
{
    public void Configure(EntityTypeBuilder<CacheEntry> b)
    {
        b.ToTable("CacheEntries");
        b.HasKey(e => e.CacheKey);
        b.Property(e => e.CacheKey).HasMaxLength(512);
        b.Property(e => e.Value).HasColumnType("nvarchar(max)");
        b.Property(e => e.ByteLength)
            .HasComputedColumnSql("CAST(DATALENGTH([Value]) AS bigint)", stored: true);
        b.Property(e => e.State).HasMaxLength(16);
        b.Property(e => e.SourceVersion).HasMaxLength(64);
        // Filtered index supports freshness scans without touching the blob.
        b.HasIndex(e => e.FetchedAt).HasDatabaseName("IX_CacheEntries_FetchedAt");
    }
}
