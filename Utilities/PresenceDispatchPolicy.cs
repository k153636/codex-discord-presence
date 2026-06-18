namespace CodexDiscordPresence;

public static class PresenceDispatchPolicy
{
    public static bool ShouldSendPresence(
        string? currentSignature,
        string? lastSentSignature,
        bool keepAliveDue,
        bool needsRefreshAfterReconnect)
    {
        if (needsRefreshAfterReconnect || keepAliveDue)
        {
            return true;
        }

        return !string.Equals(currentSignature, lastSentSignature, StringComparison.Ordinal);
    }
}
