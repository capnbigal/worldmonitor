namespace WorldMonitor.Data.Entities.Notifications;

/// <summary>Base for a user's notification channel (TPH; ChannelType is the discriminator).
/// One channel per (UserId, ChannelType) — enforced by a unique index.</summary>
public abstract class NotificationChannel
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public string ChannelType { get; set; } = null!;   // discriminator — EF sets this to the concrete type's wire value
    public bool Verified { get; set; }
    public DateTime LinkedAt { get; set; }
}

public sealed class TelegramChannel : NotificationChannel
{
    public required string ChatId { get; set; }
}

public sealed class SlackChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? SlackChannelName { get; set; }
    public string? SlackTeamName { get; set; }
    public string? SlackConfigurationUrl { get; set; }
}

public sealed class EmailChannel : NotificationChannel
{
    public required string Email { get; set; }
}

public sealed class DiscordChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? DiscordGuildId { get; set; }
    public string? DiscordChannelId { get; set; }
}

public sealed class WebhookChannel : NotificationChannel
{
    public required string WebhookEnvelope { get; set; }
    public string? WebhookLabel { get; set; }
    public string? WebhookSecret { get; set; }
}

public sealed class WebPushChannel : NotificationChannel
{
    public required string Endpoint { get; set; }
    public required string P256dh { get; set; }
    public required string Auth { get; set; }
    public string? UserAgent { get; set; }
}
