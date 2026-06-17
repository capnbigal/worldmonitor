namespace WorldMonitor.Data.Entities.Access;

/// <summary>A hashed API key for non-browser access (no premium gating). KeyHash is a SHA-256 hex digest;
/// plaintext is never stored.</summary>
public sealed class UserApiKey
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Name { get; set; }
    public required string KeyPrefix { get; set; }   // first 8 chars of the plaintext key, for display
    public required string KeyHash { get; set; }      // SHA-256 hex
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
}
