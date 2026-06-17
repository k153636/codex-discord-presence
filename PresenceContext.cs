namespace CodexDiscordPresence;

public sealed record PresenceContext(
    CodexProcessSnapshot Codex,
    ProjectSnapshot Project,
    GitSnapshot Git,
    SessionSnapshot Session,
    TokenUsageSnapshot TokenUsage);

public sealed record CodexProcessSnapshot(bool IsRunning, string? ProcessName);

public sealed record ProjectSnapshot(
    string Name,
    string Path,
    string? RecentFileName,
    string? RecentFilePath);

public sealed record GitSnapshot(bool IsGitRepository, int ChangedFileCount);

public sealed record SessionSnapshot(DateTime StartedAt, TimeSpan Elapsed);

public sealed record TokenUsageSnapshot(long? TotalTokens, decimal? EstimatedCostUsd);
