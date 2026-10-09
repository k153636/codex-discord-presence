namespace CodexDiscordPresence;

internal enum DiscordPresenceNotificationKind
{
    Connecting,
    Ready,
    Closed,
    Error,
    PresenceAcknowledged
}

internal sealed record DiscordPresenceNotification(
    DiscordPresenceNotificationKind Kind,
    DiscordPresenceSnapshot? Presence = null,
    string? ErrorCode = null);
