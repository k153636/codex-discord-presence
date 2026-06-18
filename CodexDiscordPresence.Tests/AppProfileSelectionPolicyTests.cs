using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class AppProfileSelectionPolicyTests
{
    [Fact]
    public void Select_PrefersRunningCliProfile_WhenCodexIsIdle()
    {
        var codex = new AppProfileSelectionCandidate(
            AppProfileKind.Codex,
            new CodexProcessSnapshot(false, null, false),
            new DiscordOptions { ClientId = "codex-client" });
        var cli = new AppProfileSelectionCandidate(
            AppProfileKind.CodexCli,
            new CodexProcessSnapshot(true, "codex-cli", true)
            {
                Confidence = ActivityConfidence.High,
                LastObservedAt = DateTime.UtcNow
            },
            new DiscordOptions { ClientId = "cli-client" });

        var selected = AppProfileSelectionPolicy.Select(AppProfileKind.Codex, codex, cli);

        Assert.Equal(AppProfileKind.CodexCli, selected);
    }

    [Fact]
    public void Select_FallsBackToCodex_WhenCliClientIdIsMissing()
    {
        var codex = new AppProfileSelectionCandidate(
            AppProfileKind.Codex,
            new CodexProcessSnapshot(true, "codex", true)
            {
                Confidence = ActivityConfidence.High,
                LastObservedAt = DateTime.UtcNow
            },
            new DiscordOptions { ClientId = "codex-client" });
        var cli = new AppProfileSelectionCandidate(
            AppProfileKind.CodexCli,
            new CodexProcessSnapshot(true, "codex-cli", true)
            {
                Confidence = ActivityConfidence.High,
                LastObservedAt = DateTime.UtcNow
            },
            new DiscordOptions { ClientId = "YOUR_DISCORD_CLI_APPLICATION_CLIENT_ID" });

        var selected = AppProfileSelectionPolicy.Select(AppProfileKind.Codex, codex, cli);

        Assert.Equal(AppProfileKind.Codex, selected);
    }

    [Fact]
    public void Select_PrefersNewerObservedCliProfile_WhenBothAreRunning()
    {
        var now = DateTime.UtcNow;
        var codex = new AppProfileSelectionCandidate(
            AppProfileKind.Codex,
            new CodexProcessSnapshot(true, "codex", true)
            {
                Confidence = ActivityConfidence.High,
                LastObservedAt = now.AddMinutes(-5)
            },
            new DiscordOptions { ClientId = "codex-client" });
        var cli = new AppProfileSelectionCandidate(
            AppProfileKind.CodexCli,
            new CodexProcessSnapshot(true, "codex-cli", true)
            {
                Confidence = ActivityConfidence.High,
                LastObservedAt = now
            },
            new DiscordOptions { ClientId = "cli-client" });

        var selected = AppProfileSelectionPolicy.Select(AppProfileKind.Codex, codex, cli);

        Assert.Equal(AppProfileKind.CodexCli, selected);
    }
}
