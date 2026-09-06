namespace CodexDiscordPresence;

internal sealed record SessionInspection(
    bool HasProjectPath,
    bool MatchesProject,
    bool HasTaskStarted,
    bool HasTaskCompleted,
    DateTime? LastTaskStartedAt,
    DateTime? LastTaskCompletedAt,
    DateTime? LastObservedAt,
    string? CollaborationMode,
    bool HasRunningCommand,
    string? RunningCommandReason,
    string? RefactorEvidenceReason)
{
    public string? ProjectPath { get; init; }
    public DateTime? LastShellCommandAt { get; init; }
    public RunningCommandKind LastRunningCommandKind { get; init; } = RunningCommandKind.Unknown;
    public string? LastRunningCommandName { get; init; }
    public bool LastShellCommandWasInvestigative { get; init; }
    public string? LastDirectToolFilePath { get; init; }
    public DateTime? LastDirectToolFileAt { get; init; }
    public IReadOnlyList<CodexActivityEvent> ActivityEvents { get; init; } = Array.Empty<CodexActivityEvent>();
    public IReadOnlyList<string> ActiveAgentThreadIds => CodexAgentActivityTracker.GetActiveAgentThreadIds(ActivityEvents);
    public int PartySize => 1 + ActiveAgentThreadIds.Count;
    public string? LatestThinkingSummary => ActivityEvents
        .OrderByDescending(activityEvent => activityEvent.Sequence)
        .Select(activityEvent => activityEvent.ThinkingSummary)
        .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary));

    public bool HasRecentActivity(int staleTimeoutMinutes)
    {
        var freshest = LastObservedAt ?? LastTaskStartedAt ?? LastTaskCompletedAt;
        if (!freshest.HasValue)
        {
            return false;
        }

        return DateTime.UtcNow - freshest.Value <= TimeSpan.FromMinutes(staleTimeoutMinutes);
    }

    public CodexActivityState? GetActivityStateAt(DateTime nowUtc)
    {
        return ActivityEvents.Count == 0
            ? null
            : new CodexActivityStateMachine().Evaluate(ActivityEvents, nowUtc);
    }

    public bool HasTaskCompletedSinceStart =>
        HasTaskStarted &&
        HasTaskCompleted &&
        LastTaskStartedAt.HasValue &&
        LastTaskCompletedAt.HasValue &&
        LastTaskCompletedAt >= LastTaskStartedAt;
}
