using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data.Configurations;

public sealed class FollowedCountryConfiguration : IEntityTypeConfiguration<FollowedCountry>
{
    public void Configure(EntityTypeBuilder<FollowedCountry> b)
    {
        b.ToTable("FollowedCountries");
        b.HasKey(f => f.Id);
        b.Property(f => f.UserId).HasMaxLength(128);
        b.Property(f => f.Country).HasMaxLength(2);
        b.HasIndex(f => new { f.UserId, f.Country }).IsUnique().HasDatabaseName("UX_FollowedCountries_User_Country");
        b.HasIndex(f => f.Country).HasDatabaseName("IX_FollowedCountries_Country");
    }
}
