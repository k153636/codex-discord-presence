namespace CodexDiscordPresence;

public static class PresenceUpdatePolicy
{
    public static bool ShouldSendKeepAlive(DateTime lastSuccessfulUpdateUtc, DateTime nowUtc, TimeSpan keepAliveInterval)
    {
        if (lastSuccessfulUpdateUtc == default)
        {
            return true;
        }

        return nowUtc - lastSuccessfulUpdateUtc >= keepAliveInterval;
    }
}
