namespace CodexDiscordPresence;

internal static class CodexActivityEvidence
{
    public static bool IsThinkingPhase(
        CodexActivityKind activityKind,
        CodexActivityState? activityState)
    {
        if (!activityKind.IsThinking())
        {
            return false;
        }

        // A kind-level fallback can still be returned for an unclassified
        // operation. A pending operation is stronger evidence than the
        // generic analysis label, so do not expose it as Thinking.
        if (activityState is null)
        {
            return true;
        }

        if (activityState.Lifecycle != CodexTurnLifecycle.Open ||
            activityState.PendingOperationCount > 0)
        {
            return false;
        }

        // These events represent a new reasoning phase in Codex's session
        // stream. OperationCompleted is intentionally included so the
        // presence can show Thinking while Codex decides what to do with a
        // command/MCP result, after the concrete operation grace period.
        return activityState.TriggerEvent?.Kind is null or
            CodexActivityEventKind.TurnStarted or
            CodexActivityEventKind.Reasoning or
            CodexActivityEventKind.OperationCompleted or
            CodexActivityEventKind.InputResolved;
    }

    public static bool HasFreshRecentEdits(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        int editingFreshnessSeconds)
    {
        if (recentEditedFiles.Count == 0)
        {
            return false;
        }

        var freshnessWindow = TimeSpan.FromSeconds(Math.Max(1, editingFreshnessSeconds));
        var freshest = recentEditedFiles[0].LastWriteTimeUtc;
        return DateTime.UtcNow - freshest <= freshnessWindow;
    }

    public static bool HasBurstRecentEdits(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        int changedFileCount)
    {
        if (recentEditedFiles.Count < 2 || changedFileCount < 2)
        {
            return false;
        }

        var newest = recentEditedFiles[0].LastWriteTimeUtc;
        var oldest = recentEditedFiles[^1].LastWriteTimeUtc;
        return newest - oldest <= TimeSpan.FromSeconds(3);
    }

    public static bool HasRefactorEvidence(
        GitSnapshot? gitSnapshot)
    {
        if (gitSnapshot?.LatestCommitMessage is { Length: > 0 } commitMessage &&
            ContainsAny(commitMessage, RefactorKeywords))
        {
            return true;
        }

        return false;
    }

    public static string BuildRefactorReason(
        SessionInspection? sessionInspection,
        GitSnapshot? gitSnapshot)
    {
        if (gitSnapshot?.LatestCommitMessage is { Length: > 0 } commitMessage && ContainsAny(commitMessage, RefactorKeywords))
        {
            return $"git commit message suggests refactor: {commitMessage}";
        }

        return sessionInspection?.RefactorEvidenceReason ?? "git metadata suggests refactor";
    }

    private static readonly string[] RefactorKeywords =
    [
        "refactor",
        "refactoring",
        "restructure",
        "reorganize",
        "cleanup",
        "clean up"
    ];

    private static bool ContainsAny(string value, IEnumerable<string> needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
