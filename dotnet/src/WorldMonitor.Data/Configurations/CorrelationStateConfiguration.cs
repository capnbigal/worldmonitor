using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Ml;

namespace WorldMonitor.Data.Configurations;

public sealed class CorrelationStateConfiguration : IEntityTypeConfiguration<CorrelationState>
{
    public void Configure(EntityTypeBuilder<CorrelationState> b)
    {
        b.ToTable("CorrelationStates");
        b.HasKey(s => s.Id);
        b.Property(s => s.NewsVelocity).HasColumnType("nvarchar(max)");
        b.Property(s => s.MarketChanges).HasColumnType("nvarchar(max)");
        b.Property(s => s.PredictionChanges).HasColumnType("nvarchar(max)");
    }
}
