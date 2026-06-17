namespace WorldMonitor.Data.Entities.Waitlist;

public sealed class UserReferralCode
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Code { get; set; }
    public DateTime CreatedAt { get; set; }
}
