namespace CodexDiscordPresence;

public static class PresenceUpdatePolicy
{
    internal static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(60);

    public static bool ShouldSendKeepAlive(DateTime lastSuccessfulUpdateUtc, DateTime nowUtc, TimeSpan keepAliveInterval)
    {
        if (lastSuccessfulUpdateUtc == default)
        {
            return true;
        }

        return nowUtc - lastSuccessfulUpdateUtc >= keepAliveInterval;
    }
}
