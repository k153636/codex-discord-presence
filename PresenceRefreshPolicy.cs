namespace CodexDiscordPresence;

public static class PresenceRefreshPolicy
{
    public static TimeSpan GetNextDelay(PresenceTemplateOptions options, CodexActivityKind activityKind)
    {
        return TimeSpan.FromMilliseconds(250);
    }
}
