namespace CodexDiscordPresence;

public static class DiscordReconnectBackoff
{
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromSeconds(3),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public static TimeSpan GetDelay(int failedAttempts)
    {
        if (failedAttempts <= 0)
        {
            return Delays[0];
        }

        return Delays[Math.Min(failedAttempts - 1, Delays.Length - 1)];
    }
}
