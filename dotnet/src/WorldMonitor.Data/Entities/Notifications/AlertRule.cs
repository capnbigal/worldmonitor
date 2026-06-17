namespace WorldMonitor.Data.Entities.Notifications;

/// <summary>A user's per-variant alerting rule. Array fields are JSON (EF primitive collections);
/// enum-like fields hold the wire literal strings. AiDigestEnabled is INERT (no generative AI).</summary>
public sealed class AlertRule
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Variant { get; set; }
    public bool Enabled { get; set; }
    public List<string> EventTypes { get; set; } = [];
    public string Sensitivity { get; set; } = "all";        // all|high|critical
    public List<string> Channels { get; set; } = [];        // channelType values
    public DateTime UpdatedAt { get; set; }
    public bool? QuietHoursEnabled { get; set; }
    public int? QuietHoursStart { get; set; }
    public int? QuietHoursEnd { get; set; }
    public string? QuietHoursTimezone { get; set; }
    public string? QuietHoursOverride { get; set; }         // critical_only|silence_all|batch_on_wake
    public string? DigestMode { get; set; }                 // realtime|daily|twice_daily|weekly
    public int? DigestHour { get; set; }
    public string? DigestTimezone { get; set; }
    public bool? AiDigestEnabled { get; set; }              // inert
    public List<string> Countries { get; set; } = [];
}
