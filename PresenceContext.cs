namespace CodexDiscordPresence;

public sealed record PresenceContext(
    string ModelName,
    CodexProcessSnapshot Codex,
    ProjectSnapshot Project,
    GitSnapshot Git,
    SessionSnapshot Session,
    TokenUsageSnapshot TokenUsage);

public sealed record CodexProcessSnapshot(bool IsRunning, string? ProcessName, bool IsThinking);

public sealed record ProjectSnapshot(
    string Name,
    string Path,
    string? RecentFileName,
    string? RecentFilePath,
    int ScannedFileCount,
    long TotalLineCount,
    IReadOnlyList<RecentProjectFileSnapshot> RecentFiles);

public sealed record GitSnapshot(bool IsGitRepository, int ChangedFileCount);

public sealed record SessionSnapshot(DateTime StartedAt, TimeSpan Elapsed);

public sealed record TokenUsageSnapshot(long? TotalTokens, decimal? EstimatedCostUsd);

public sealed record RecentProjectFileSnapshot(string Name, string Path, DateTime LastWriteTimeUtc);
