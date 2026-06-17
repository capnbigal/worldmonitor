namespace WorldMonitor.Data.Entities.Identity;

/// <summary>An authenticated principal. UserId is the external subject id (Clerk today; the .NET
/// principal source is decided in P3). Email/NormalizedEmail are server-derived.</summary>
public sealed class User
{
    public required string UserId { get; set; }
    public string? Email { get; set; }
    public string? NormalizedEmail { get; set; }
    public string? LocaleTag { get; set; }
    public string? LocalePrimary { get; set; }
    public string? Timezone { get; set; }
    public string? Country { get; set; }            // ISO 3166-1 alpha-2; client-reported
    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
