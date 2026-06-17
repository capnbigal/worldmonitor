using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class VectorEntryConfiguration : IEntityTypeConfiguration<VectorEntry>
{
    public void Configure(EntityTypeBuilder<VectorEntry> b)
    {
        b.ToTable("Vectors");
        b.HasKey(v => v.Id);
        b.Property(v => v.Id).HasMaxLength(128);
        b.Property(v => v.Text).HasMaxLength(200);
        b.Property(v => v.Embedding).HasColumnType("varbinary(1536)");
        b.Property(v => v.Source).HasMaxLength(64);
        b.Property(v => v.Url).HasMaxLength(2048);
        b.HasIndex(v => v.IngestedAt).HasDatabaseName("IX_Vectors_IngestedAt");
    }
}
