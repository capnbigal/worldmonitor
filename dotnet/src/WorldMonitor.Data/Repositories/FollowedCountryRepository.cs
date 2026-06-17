using Microsoft.EntityFrameworkCore;
using WorldMonitor.Data.Entities.Watchlist;

namespace WorldMonitor.Data.Repositories;

public enum FollowResult { Followed, AlreadyFollowing, CapReached }

/// <param name="MaxPerUser">Per-user follow cap (legacy: 50).</param>
/// <param name="PrivacyFloor">Follower counts below this are reported as 0 (legacy: 5).</param>
public sealed record WatchlistOptions(int MaxPerUser = 50, int PrivacyFloor = 5);

public sealed class FollowedCountryRepository(WorldMonitorDbContext db, WatchlistOptions options)
{
    private static string Norm(string country) => country.Trim().ToUpperInvariant();

    public async Task<FollowResult> FollowAsync(string userId, string country, CancellationToken ct = default)
    {
        var c = Norm(country);
        if (await db.FollowedCountries.AnyAsync(f => f.UserId == userId && f.Country == c, ct))
            return FollowResult.AlreadyFollowing;
        // Best-effort cap; the UNIQUE(UserId,Country) constraint is the hard correctness guard.
        if (await db.FollowedCountries.CountAsync(f => f.UserId == userId, ct) >= options.MaxPerUser)
            return FollowResult.CapReached;

        db.FollowedCountries.Add(new FollowedCountry { UserId = userId, Country = c, AddedAt = DateTime.UtcNow });
        try
        {
            await db.SaveChangesAsync(ct);
            return FollowResult.Followed;
        }
        catch (DbUpdateException) // concurrent duplicate lost the unique race
        {
            return FollowResult.AlreadyFollowing;
        }
    }

    public async Task<bool> UnfollowAsync(string userId, string country, CancellationToken ct = default)
    {
        var c = Norm(country);
        var removed = await db.FollowedCountries
            .Where(f => f.UserId == userId && f.Country == c)
            .ExecuteDeleteAsync(ct);
        return removed > 0;
    }

    public Task<int> CountForUserAsync(string userId, CancellationToken ct = default)
        => db.FollowedCountries.CountAsync(f => f.UserId == userId, ct);

    /// <summary>The single place the privacy floor is applied. Counts below the floor read as 0.</summary>
    public async Task<int> CountFollowersAsync(string country, CancellationToken ct = default)
    {
        var n = await db.FollowedCountries.CountAsync(f => f.Country == Norm(country), ct);
        return n < options.PrivacyFloor ? 0 : n;
    }
}
