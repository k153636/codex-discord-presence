using System.Globalization;

namespace CodexDiscordPresence;

internal static class PresenceActivityComposer
{
    public static string BuildActivityLine(
        PresenceContext context,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        string stateLabel,
        RecentProjectFileSnapshot? editingFile,
        string freshnessElapsedText)
    {
        if (!context.Codex.IsRunning)
        {
            return stateLabel;
        }

        if (context.Codex.ActivityKind == CodexActivityKind.UpdatingFiles)
        {
            return AppendActivityElapsed(stateLabel, freshnessElapsedText);
        }

        if (context.Codex.ActivityKind is CodexActivityKind.ApplyingEdits or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles &&
            recentEditedFiles.Count > 0)
        {
            return AppendActivityElapsed(BuildEditingActivityLine(stateLabel, recentEditedFiles, editingFile), freshnessElapsedText);
        }

        return context.Codex.ActivityKind.IsActive()
            ? AppendActivityElapsed(BuildIdleActivityLine(context, stateLabel), freshnessElapsedText)
            : BuildIdleActivityLine(context, stateLabel);
    }

    public static string BuildFreshnessElapsedText(PresenceContext context, int freshnessIntervalSeconds)
    {
        var referenceUtc = context.Codex.LastObservedAt ?? context.Session.StartedAt;
        var elapsed = DateTime.UtcNow - referenceUtc;
        var bucketedElapsed = BucketDuration(elapsed, freshnessIntervalSeconds);
        return FormatShortDuration(bucketedElapsed, allowZero: true);
    }

    private static string BuildEditingActivityLine(
        string stateLabel,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        RecentProjectFileSnapshot? editingFile)
    {
        if (editingFile is not null && recentEditedFiles.Count <= 4)
        {
            return $"{stateLabel} \u2022 {editingFile.Name}";
        }

        return stateLabel;
    }

    private static string BuildIdleActivityLine(
        PresenceContext context,
        string stateLabel)
    {
        return context.Codex.ActivityKind switch
        {
            CodexActivityKind.Planning => stateLabel,
            CodexActivityKind.ApplyingEdits => stateLabel,
            CodexActivityKind.UpdatingFiles => stateLabel,
            CodexActivityKind.CreatingFiles => stateLabel,
            CodexActivityKind.DeletingFiles => stateLabel,
            CodexActivityKind.Refactoring => stateLabel,
            CodexActivityKind.AnalyzingProject => stateLabel,
            CodexActivityKind.RunningCommand => stateLabel,
            _ => stateLabel
        };
    }

    private static string AppendActivityElapsed(string baseLine, string elapsedText)
    {
        if (string.IsNullOrWhiteSpace(baseLine))
        {
            return baseLine;
        }

        return string.IsNullOrWhiteSpace(elapsedText)
            ? baseLine
            : $"{baseLine} \u2022 {elapsedText}";
    }

    private static TimeSpan BucketDuration(TimeSpan elapsed, int intervalSeconds)
    {
        if (elapsed <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(1, intervalSeconds));
        var bucketCount = Math.Floor(elapsed.TotalSeconds / interval.TotalSeconds);
        return TimeSpan.FromSeconds(bucketCount * interval.TotalSeconds);
    }

    private static string FormatShortDuration(TimeSpan elapsed, bool allowZero = false)
    {
        if (elapsed.TotalHours >= 1)
        {
            return elapsed.Minutes == 0
                ? $"{(int)elapsed.TotalHours}h"
                : $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        }

        if (elapsed.TotalMinutes >= 1)
        {
            return elapsed.Seconds == 0
                ? $"{(int)elapsed.TotalMinutes}m"
                : $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s";
        }

        var seconds = (int)elapsed.TotalSeconds;
        return $"{Math.Max(allowZero ? 0 : 1, seconds)}s";
    }
}
