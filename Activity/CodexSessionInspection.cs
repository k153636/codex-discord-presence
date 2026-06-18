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

    public bool HasRecentActivity(int staleTimeoutMinutes)
    {
        var freshest = LastObservedAt ?? LastTaskStartedAt ?? LastTaskCompletedAt;
        if (!freshest.HasValue)
        {
            return false;
        }

        return DateTime.UtcNow - freshest.Value <= TimeSpan.FromMinutes(staleTimeoutMinutes);
    }

    public bool HasTaskCompletedSinceStart =>
        HasTaskStarted &&
        HasTaskCompleted &&
        LastTaskStartedAt.HasValue &&
        LastTaskCompletedAt.HasValue &&
        LastTaskCompletedAt >= LastTaskStartedAt;
}
