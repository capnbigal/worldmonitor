namespace WorldMonitor.Data.Entities.Notifications;

public sealed class TelegramPairingToken
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public required string Token { get; set; }
    public DateTime ExpiresAt { get; set; }
    public bool Used { get; set; }
    public string? Variant { get; set; }
}
