namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>A waitlist registration. <see cref="Id"/> is an IDENTITY giving monotonic registration order
/// (replaces the legacy `counters` table); the gap-free position is COUNT(Id &lt;= mine).</summary>
public sealed class Registration
{
    public int Id { get; set; }
    public required string Email { get; set; }
    public required string NormalizedEmail { get; set; }
    public DateTime RegisteredAt { get; set; }
    public string? Source { get; set; }
    public string? AppVersion { get; set; }
    public string? ReferralCode { get; set; }
    public string? ReferredBy { get; set; }
    public int? ReferralCount { get; set; }
}
