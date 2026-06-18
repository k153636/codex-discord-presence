namespace CodexDiscordPresence;

public sealed record AppProfileSelectionCandidate(
    AppProfileKind Profile,
    CodexProcessSnapshot Snapshot,
    DiscordOptions DiscordOptions)
{
    public bool HasValidDiscordClientId =>
        !string.IsNullOrWhiteSpace(DiscordOptions.ClientId) &&
        !DiscordOptions.ClientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);

    public bool IsRunning => HasValidDiscordClientId && Snapshot.IsRunning;

    public int DetectionStrength => Snapshot.DetectionKind switch
    {
        CodexProcessDetectionKind.CommandLine => 400,
        CodexProcessDetectionKind.ExecutablePath => 300,
        CodexProcessDetectionKind.WindowTitle => 200,
        CodexProcessDetectionKind.ProcessName => 100,
        CodexProcessDetectionKind.SessionActivity => 50,
        _ => 0
    };
}

public static class AppProfileSelectionPolicy
{
    public static AppProfileKind Select(
        AppProfileKind currentProfile,
        AppProfileSelectionCandidate codexCandidate,
        AppProfileSelectionCandidate cliCandidate)
    {
        if (codexCandidate.HasValidDiscordClientId != cliCandidate.HasValidDiscordClientId)
        {
            return codexCandidate.HasValidDiscordClientId
                ? AppProfileKind.Codex
                : AppProfileKind.CodexCli;
        }

        if (codexCandidate.DetectionStrength != cliCandidate.DetectionStrength)
        {
            return codexCandidate.DetectionStrength > cliCandidate.DetectionStrength
                ? AppProfileKind.Codex
                : AppProfileKind.CodexCli;
        }

        if (codexCandidate.IsRunning != cliCandidate.IsRunning)
        {
            return codexCandidate.IsRunning
                ? AppProfileKind.Codex
                : AppProfileKind.CodexCli;
        }

        if (codexCandidate.IsRunning && cliCandidate.IsRunning)
        {
            var codexObservedAt = codexCandidate.Snapshot.LastObservedAt;
            var cliObservedAt = cliCandidate.Snapshot.LastObservedAt;
            if (codexObservedAt.HasValue != cliObservedAt.HasValue)
            {
                return codexObservedAt.HasValue
                    ? AppProfileKind.Codex
                    : AppProfileKind.CodexCli;
            }

            if (codexObservedAt.HasValue && cliObservedAt.HasValue)
            {
                if (codexObservedAt.Value > cliObservedAt.Value)
                {
                    return AppProfileKind.Codex;
                }

                if (cliObservedAt.Value > codexObservedAt.Value)
                {
                    return AppProfileKind.CodexCli;
                }
            }

            if (codexCandidate.Snapshot.Confidence != cliCandidate.Snapshot.Confidence)
            {
                return codexCandidate.Snapshot.Confidence < cliCandidate.Snapshot.Confidence
                    ? AppProfileKind.Codex
                    : AppProfileKind.CodexCli;
            }

            return currentProfile == AppProfileKind.CodexCli ? AppProfileKind.CodexCli : AppProfileKind.Codex;
        }

        return currentProfile == AppProfileKind.CodexCli ? AppProfileKind.CodexCli : AppProfileKind.Codex;
    }
}
