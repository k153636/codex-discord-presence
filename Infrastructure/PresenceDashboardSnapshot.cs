namespace CodexDiscordPresence;

public sealed record PresenceDashboardSnapshot(
    AppProfileKind Profile,
    string? ModelName,
    string? ProjectName,
    RenderedPresence? Presence,
    TokenUsageSnapshot? TokenUsage,
    bool IsDiscordConnected,
    DateTime UpdatedAtUtc)
{
    public static PresenceDashboardSnapshot Empty { get; } = new(
        AppProfileKind.Codex,
        null,
        null,
        null,
        null,
        false,
        DateTime.MinValue);
}
