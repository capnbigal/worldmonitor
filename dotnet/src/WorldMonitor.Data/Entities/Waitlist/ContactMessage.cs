namespace WorldMonitor.Data.Entities.Waitlist;

public sealed class ContactMessage
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Email { get; set; }
    public string? Organization { get; set; }
    public string? Phone { get; set; }
    public string? Message { get; set; }
    public required string Source { get; set; }
    public DateTime ReceivedAt { get; set; }
    public string? NormalizedEmail { get; set; }
}
