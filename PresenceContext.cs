namespace CodexDiscordPresence;

public sealed record PresenceContext(
    string ModelName,
    CodexProcessSnapshot Codex,
    ProjectSnapshot Project,
    GitSnapshot Git,
    SessionSnapshot Session,
    TokenUsageSnapshot TokenUsage);

public sealed partial record CodexProcessSnapshot(bool IsRunning, string? ProcessName, bool IsThinking);

public enum CodexActivityKind
{
    Offline = 0,
    Ready = 1,
    AnalyzingProject = 2,
    ApplyingEdits = 3,
    UpdatingFiles = 4,
    CreatingFiles = 5,
    DeletingFiles = 6,
    RunningCommand = 7,
    Planning = 8,
    Refactoring = 9
}

public enum ActivityConfidence
{
    High = 0,
    Low = 1
}

public enum ActivityProvenance
{
    Observed = 0,
    Inferred = 1,
    Mixed = 2
}

public static class CodexActivityKindExtensions
{
    public static bool IsActive(this CodexActivityKind kind)
    {
        return kind is not CodexActivityKind.Offline and not CodexActivityKind.Ready;
    }
}

public sealed partial record CodexProcessSnapshot
{
    public CodexActivityKind? DetectedActivityKind { get; init; }
    public ActivityConfidence Confidence { get; init; } = ActivityConfidence.High;
    public ActivityProvenance ActivityProvenance { get; init; } = ActivityProvenance.Inferred;
    public string ActivityReason { get; init; } = "";
    public DateTime? LastObservedAt { get; init; }
    public IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles { get; init; } = Array.Empty<RecentProjectFileSnapshot>();

    public CodexActivityKind ActivityKind =>
        DetectedActivityKind ??
        (IsRunning
            ? (IsThinking ? CodexActivityKind.AnalyzingProject : CodexActivityKind.Ready)
            : CodexActivityKind.Offline);
}

public sealed record ProjectSnapshot(
    string Name,
    string Path,
    string? RecentFileName,
    string? RecentFilePath,
    int TotalFileCount,
    int ScannedFileCount,
    long TotalLineCount,
    IReadOnlyList<RecentProjectFileSnapshot> RecentFiles);

public sealed record GitSnapshot(
    bool IsGitRepository,
    int ChangedFileCount,
    string? LatestCommitMessage,
    int CreatedFileCount = 0,
    int DeletedFileCount = 0);

public sealed record SessionSnapshot(DateTime StartedAt, TimeSpan Elapsed);

public sealed record TokenUsageSnapshot(long? TotalTokens, decimal? EstimatedCostUsd);

public sealed record RecentProjectFileSnapshot(string Name, string Path, DateTime LastWriteTimeUtc);
