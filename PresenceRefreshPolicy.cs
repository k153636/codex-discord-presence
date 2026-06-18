namespace CodexDiscordPresence;

public static class PresenceRefreshPolicy
{
    public static TimeSpan GetNextDelay(PresenceTemplateOptions options, CodexActivityKind activityKind)
    {
        return TimeSpan.FromSeconds(1);
    }
}
