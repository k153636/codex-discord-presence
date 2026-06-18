namespace CodexDiscordPresence;

public static class PresenceRefreshPolicy
{
    public static TimeSpan GetNextDelay(
        PresenceTemplateOptions options,
        CodexActivityKind activityKind,
        int defaultUpdateIntervalSeconds)
    {
        var defaultInterval = TimeSpan.FromSeconds(Math.Max(1, defaultUpdateIntervalSeconds));
        var activeInterval = TimeSpan.FromSeconds(Math.Max(1, options.ActiveUpdateIntervalSeconds));
        var idleInterval = TimeSpan.FromSeconds(Math.Max(1, options.IdleUpdateIntervalSeconds));
        var freshnessInterval = TimeSpan.FromSeconds(Math.Max(1, options.FreshnessUpdateIntervalSeconds));
        var runningCommandInterval = options.RunningCommandUpdateIntervalMilliseconds > 0
            ? TimeSpan.FromMilliseconds(options.RunningCommandUpdateIntervalMilliseconds)
            : TimeSpan.FromSeconds(Math.Max(1, options.RunningCommandUpdateIntervalSeconds));

        return activityKind switch
        {
            CodexActivityKind.RunningCommand => runningCommandInterval,
            CodexActivityKind.AnalyzingProject => freshnessInterval,
            CodexActivityKind.ApplyingEdits or CodexActivityKind.UpdatingFiles or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles => activeInterval,
            CodexActivityKind.Planning or CodexActivityKind.Refactoring => activeInterval,
            CodexActivityKind.Ready or CodexActivityKind.Offline => idleInterval,
            _ => defaultInterval
        };
    }
}
