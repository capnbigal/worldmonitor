using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class TopicVelocityPointConfiguration : IEntityTypeConfiguration<TopicVelocityPoint>
{
    public void Configure(EntityTypeBuilder<TopicVelocityPoint> b)
    {
        b.ToTable("TopicVelocityPoints");
        b.HasKey(p => p.Id);
        b.Property(p => p.Topic).HasMaxLength(128);
        b.HasIndex(p => new { p.Topic, p.Timestamp }).HasDatabaseName("IX_TopicVelocity_Topic_Timestamp");
    }
}
