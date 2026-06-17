using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class CorrelationClusterStateConfiguration : IEntityTypeConfiguration<CorrelationClusterState>
{
    public void Configure(EntityTypeBuilder<CorrelationClusterState> b)
    {
        b.ToTable("CorrelationClusterStates");
        b.HasKey(c => c.Id);
        b.Property(c => c.Domain).HasMaxLength(64);
        b.Property(c => c.ClusterKey).HasMaxLength(256);
        b.Property(c => c.Country).HasMaxLength(2);
        b.Property(c => c.EntityKey).HasMaxLength(256);
        b.HasIndex(c => new { c.Domain, c.ClusterKey }).IsUnique().HasDatabaseName("UX_CorrelationClusters_Domain_Key");
    }
}
