using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceStatusLabelResolverTests
{
    [Fact]
    public void ResolveStateLabel_RunningCommandWithTaskStarted_ReturnsRunningCommand()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.RunningCommand,
                LastTaskStartedAt = DateTime.UtcNow
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.RunningCommand, 0);

        Assert.Equal("Running command", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithTaskStarted_ReturnsWorking()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                LastTaskStartedAt = DateTime.UtcNow
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Working", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithoutStrongEvidence_ReturnsThinking()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true));

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Thinking", label);
    }

    [Fact]
    public void ResolveStateLabel_ReadyWithinGracePeriod_ReturnsWaiting()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            sessionAge: TimeSpan.FromMinutes(4));

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.Ready, 0);

        Assert.Equal("Waiting", label);
    }

    [Fact]
    public void ResolveStateLabel_ReadyAfterTaskCompleted_ReturnsInput()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                ActivityReason = "task_complete without file writes",
                LastObservedAt = DateTime.UtcNow.AddMinutes(-1)
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.Ready, 0);

        Assert.Equal("Input", label);
    }

    [Fact]
    public void ResolveStateLabel_Offline_ReturnsIdling()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(false, "codex", false));

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.Offline, 0);

        Assert.Equal("Idling", label);
    }

    private static PresenceContext CreateContext(
        CodexProcessSnapshot codex,
        TimeSpan? sessionAge = null,
        DateTime? lastObservedAt = null)
    {
        var startedAt = DateTime.UtcNow - (sessionAge ?? TimeSpan.FromMinutes(5));

        return new PresenceContext(
            "gpt-5-codex",
            lastObservedAt.HasValue
                ? codex with { LastObservedAt = lastObservedAt }
                : codex,
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null),
            new SessionSnapshot(startedAt, DateTime.UtcNow - startedAt),
            new TokenUsageSnapshot(null, null));
    }
}
