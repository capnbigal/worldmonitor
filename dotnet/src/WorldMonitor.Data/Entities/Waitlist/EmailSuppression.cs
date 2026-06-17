namespace WorldMonitor.Data.Entities.Waitlist;

/// <summary>A suppressed email address. Reason is the wire literal: bounce|complaint|manual.
/// Read by the registration flow.</summary>
public sealed class EmailSuppression
{
    public int Id { get; set; }
    public required string NormalizedEmail { get; set; }
    public required string Reason { get; set; }   // bounce|complaint|manual
    public DateTime SuppressedAt { get; set; }
    public string? Source { get; set; }
}
