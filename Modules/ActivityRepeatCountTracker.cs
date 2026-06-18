namespace CodexDiscordPresence;

internal static class ActivityRepeatCountTracker
{
    public static int GetAnalyzingRepeatCount(
        CodexActivityKind currentActivityKind,
        CodexActivityKind lastActivityKind,
        DateTime? currentObservedAt,
        DateTime? lastAnalyzingObservedAt,
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

        if (!currentObservedAt.HasValue || !lastAnalyzingObservedAt.HasValue)
        {
            return lastAnalyzingRepeatCount;
        }

        if (currentObservedAt.Value == lastAnalyzingObservedAt.Value)
        {
            return lastAnalyzingRepeatCount;
        }

        return lastAnalyzingRepeatCount + 1;
    }
}
