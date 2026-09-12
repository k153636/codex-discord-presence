namespace CodexDiscordPresence;

public interface IPresenceActivitySnapshot
{
    bool IsRunning { get; }
    string? ProcessName { get; }
    bool IsThinking { get; }
    CodexActivityKind ActivityKind { get; }
    ActivityConfidence Confidence { get; }
    ActivityProvenance ActivityProvenance { get; }
    string ActivityReason { get; }
    string? CollaborationMode { get; }
    DateTime? LastTaskStartedAt { get; }
    DateTime? ActivityStartedAt { get; }
    DateTime? LastObservedAt { get; }
    RunningCommandKind RunningCommandKind { get; }
    string RunningCommandName { get; }
    string? LastDirectToolFilePath { get; }
    DateTime? LastDirectToolFileAt { get; }
    string? ActiveToolFilePath { get; }
    bool IsMcpOperation { get; }
    string? McpServerName { get; }
    IReadOnlyList<string> ActiveMcpServerNames { get; }
    CodexActivityEventKind? LatestActivityEventKind { get; }
    IReadOnlyList<string> ActivityFilePaths { get; }
    string? ActiveActivityDescription { get; }
    int PendingOperationCount { get; }
    int PendingMutationCount { get; }
    string? ActiveTurnId { get; }
    DateTime? LastEffectiveSignalAt { get; }
    string? LatestThinkingSummary { get; }
    int? PartySize { get; }
    bool IsSuccessfulCompletion { get; }
    bool IsError { get; }
    bool HasDirectActivityEvidence { get; }
    IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles { get; }
    int ActivityRepeatCount { get; }
    string? ProviderState { get; }
}

public sealed record PresenceContext(
    string ModelName,
    IPresenceActivitySnapshot Activity,
    ProjectSnapshot Project,
    GitSnapshot Git,
    SessionSnapshot Session,
    TokenUsageSnapshot TokenUsage)
{
    public string? ProviderId { get; init; }
    public string? ExecutionMode { get; init; }
    public string? ModelReasoningLevel { get; init; }
    public string? ModelVariant { get; init; }
}

public enum CodexProcessDetectionKind
{
    None = 0,
    ProcessName = 1,
    WindowTitle = 2,
    ExecutablePath = 3,
    CommandLine = 4,
    SessionActivity = 5
}

public sealed partial record CodexProcessSnapshot(bool IsRunning, string? ProcessName, bool IsThinking) : IPresenceActivitySnapshot;

public enum CodexActivityKind
{
    Offline = 0,
    Ready = 1,
    AnalyzingProject = 2,
    ApplyingEdits = 3,
    CoordinatingChanges = 4,
    CreatingFiles = 5,
    DeletingFiles = 6,
    RunningCommand = 7,
    Planning = 8,
    Refactoring = 9,
    WaitingForInput = 10,
    ReadingFiles = 11,
    Stalled = 12,
    Researching = 13
}

public enum RunningCommandKind
{
    Unknown = 0,
    Git = 1,
    Search = 2,
    Build = 3,
    Test = 4
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
        return kind is not (CodexActivityKind.Offline or CodexActivityKind.Ready or CodexActivityKind.WaitingForInput or CodexActivityKind.Stalled);
    }

    public static bool IsThinking(this CodexActivityKind kind)
    {
        return kind is CodexActivityKind.AnalyzingProject or CodexActivityKind.Planning;
    }
}

public sealed partial record CodexProcessSnapshot
{
    internal SessionInspection? SessionInspection { get; init; }
    public CodexActivityKind? DetectedActivityKind { get; init; }
    public string? ProviderState => null;
    public ActivityConfidence Confidence { get; init; } = ActivityConfidence.High;
    public ActivityProvenance ActivityProvenance { get; init; } = ActivityProvenance.Inferred;
    public string ActivityReason { get; init; } = "";
    public string? CollaborationMode { get; init; }
    public string? ObservedProjectPath { get; init; }
    public DateTime? LastTaskStartedAt { get; init; }
    public DateTime? ActivityStartedAt { get; init; }
    public DateTime? LastObservedAt { get; init; }
    public DateTime? LastShellCommandAt { get; init; }
    public RunningCommandKind RunningCommandKind { get; init; } = RunningCommandKind.Unknown;
    public string RunningCommandName { get; init; } = "";
    public string? LastDirectToolFilePath { get; init; }
    public DateTime? LastDirectToolFileAt { get; init; }
    public string? ActiveToolFilePath { get; init; }
    public string? ActiveActivityDescription => null;
    public bool IsMcpOperation { get; init; }
    public string? McpServerName { get; init; }
    public IReadOnlyList<string> ActiveMcpServerNames { get; init; } = Array.Empty<string>();
    public CodexActivityEventKind? LatestActivityEventKind { get; init; }
    public IReadOnlyList<string> ActivityFilePaths { get; init; } = Array.Empty<string>();
    public int PendingOperationCount { get; init; }
    public int PendingMutationCount { get; init; }
    public string? ActiveTurnId { get; init; }
    public DateTime? LastEffectiveSignalAt { get; init; }
    public string? LatestThinkingSummary { get; init; }
    public int? PartySize { get; init; }
    public bool IsSuccessfulCompletion { get; init; }
    public bool IsError { get; init; }
    public bool HasDirectActivityEvidence { get; init; }
    internal CodexTurnLifecycle TurnLifecycle { get; init; } = CodexTurnLifecycle.None;
    public IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles { get; init; } = Array.Empty<RecentProjectFileSnapshot>();
    public int ActivityRepeatCount { get; init; } = 1;
    public CodexProcessDetectionKind DetectionKind { get; init; } = CodexProcessDetectionKind.None;

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

public sealed record TokenUsageSnapshot(
    long? TotalTokens,
    decimal? EstimatedCostUsd,
    string? BillingType = null,
    RateLimitSnapshot? RateLimit = null);

public sealed record RecentProjectFileSnapshot(string Name, string Path, DateTime LastWriteTimeUtc);
