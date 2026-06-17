namespace WorldMonitor.Data.Entities.Identity;

/// <summary>Per-user, per-variant preference blob. <see cref="SyncVersion"/> implements a
/// client-supplied compare-and-set (see UserPreferenceRepository), NOT EF optimistic concurrency.</summary>
public sealed class UserPreference
{
    public int Id { get; set; }                     // surrogate PK
    public required string UserId { get; set; }
    public required string Variant { get; set; }
    public required string Data { get; set; }       // JSON blob (nvarchar(max))
    public int SchemaVersion { get; set; }
    public DateTime UpdatedAt { get; set; }
    public int SyncVersion { get; set; }
}
