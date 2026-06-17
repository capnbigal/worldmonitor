namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>One attribution row per (referrer, referee email).</summary>
public sealed class UserReferralCredit
{
    public int Id { get; set; }
    public required string ReferrerUserId { get; set; }
    public required string RefereeEmail { get; set; }
    public DateTime CreatedAt { get; set; }
}
