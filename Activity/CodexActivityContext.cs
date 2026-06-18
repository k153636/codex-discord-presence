namespace CodexDiscordPresence;

internal sealed record CodexActivityContext(
    IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles,
    int ChangedFileCount,
    SessionInspection? SessionInspection,
    GitSnapshot? GitSnapshot,
    CodexActivityKind? PreviousActivityKind,
    int ThinkingStaleTimeoutMinutes,
    int EditingFreshnessSeconds);
