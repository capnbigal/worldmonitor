namespace WorldMonitor.Data.Entities.Watchlist;

/// <summary>A country a user follows. UNIQUE(UserId, Country) is the hard guard against duplicate
/// follows and replaces the legacy Convex OCC shard/lock/meta scaffolding.</summary>
public sealed class FollowedCountry
{
    public int Id { get; set; }                     // surrogate PK
    public required string UserId { get; set; }
    public required string Country { get; set; }    // ISO 3166-1 alpha-2, uppercase
    public DateTime AddedAt { get; set; }
}
