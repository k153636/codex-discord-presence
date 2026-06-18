namespace CodexDiscordPresence;

internal static class ActivityRepeatCountTracker
{
    public static int GetAnalyzingRepeatCount(
        CodexActivityKind currentActivityKind,
        CodexActivityKind lastActivityKind,
        DateTime? currentTaskStartedAt,
        DateTime? lastAnalyzingTaskStartedAt,
        int lastAnalyzingRepeatCount)
    {
        if (currentActivityKind != CodexActivityKind.AnalyzingProject)
        {
            return 1;
        }

        if (lastActivityKind != CodexActivityKind.AnalyzingProject)
        {
            return 1;
        }

        if (!currentTaskStartedAt.HasValue)
        {
            return lastAnalyzingRepeatCount;
        }

        if (!lastAnalyzingTaskStartedAt.HasValue)
        {
            return lastAnalyzingRepeatCount + 1;
        }

        if (currentTaskStartedAt.Value <= lastAnalyzingTaskStartedAt.Value)
        {
            return lastAnalyzingRepeatCount;
        }

        return lastAnalyzingRepeatCount + 1;
    }
}
