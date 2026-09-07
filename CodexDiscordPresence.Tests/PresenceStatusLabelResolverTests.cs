using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceStatusLabelResolverTests
{
    [Fact]
    public void ResolveStateLabel_RunningCommandWithGitName_ReturnsRunCommandGit()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.RunningCommand,
                RunningCommandKind = RunningCommandKind.Git,
                RunningCommandName = "git",
                LastTaskStartedAt = DateTime.UtcNow
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.RunningCommand, 0);

        Assert.Equal("Run Command: git", label);
    }

    [Fact]
    public void ResolveStateLabel_RunningCommandWithoutName_ReturnsRunCommand()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.RunningCommand,
                RunningCommandKind = RunningCommandKind.Search,
                LastTaskStartedAt = DateTime.UtcNow
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.RunningCommand, 0);

        Assert.Equal("Run Command", label);
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
    public void ResolveStateLabel_AnalyzingProjectWithThinkingSummary_ReturnsSummaryInsteadOfWorking()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                LastTaskStartedAt = DateTime.UtcNow,
                LatestThinkingSummary = "**Designing mobile-friendly file label format**"
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Designing mobile-friendly file label format", label);
    }

    [Fact]
    public void ResolveStateLabel_CompletedOperationUsesCurrentThinkingSummary()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.AnalyzingProject,
                LatestActivityEventKind = CodexActivityEventKind.OperationCompleted,
                LatestThinkingSummary = "Previous reasoning summary"
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Previous reasoning summary", label);
    }

    [Fact]
    public void ResolveStateLabel_ExplicitReasoningWithoutSummary_ReturnsThinking()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.AnalyzingProject,
                LatestActivityEventKind = CodexActivityEventKind.Reasoning
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Thinking", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithSearchEvidenceAndThinkingSummaryPrefersSummary()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                LastTaskStartedAt = DateTime.UtcNow,
                LatestThinkingSummary = "Reviewing the current MCP activity"
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Reviewing the current MCP activity", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithTaskStartedIgnoresGenericFallbackLabels()
    {
        var resolver = new PresenceStatusLabelResolver();
        var template = new PresenceTemplateOptions
        {
            WorkingText = "Working",
            ThinkingText = "Thinking",
            AnalyzingProjectText = "Thinking",
            AnalyzingText = "Thinking"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                LastTaskStartedAt = DateTime.UtcNow
            });

        var label = resolver.ResolveStateLabel(template, context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Working", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithoutStrongEvidence_ReturnsWorking()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true));

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Working", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithoutThinkingEvidence_ReturnsWaiting()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.AnalyzingProject
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Waiting", label);
    }

    [Fact]
    public void ResolveStateLabel_AnalyzingProjectWithSearchEvidence_ReturnsWorking()
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
    public void ResolveStateLabel_AnalyzingProjectWithSearchCommandName_ReturnsRunCommand()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                RunningCommandKind = RunningCommandKind.Search,
                RunningCommandName = "rg"
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.AnalyzingProject, 0);

        Assert.Equal("Run Command: rg", label);
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
    public void ResolveStateLabel_ReadyUsesActivityStartTimeForGracePeriod()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                ActivityStartedAt = DateTime.UtcNow.AddMinutes(-4),
                LastObservedAt = DateTime.UtcNow.AddHours(-1)
            },
            sessionAge: TimeSpan.FromHours(1),
            lastObservedAt: DateTime.UtcNow.AddHours(-1));

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.Ready, 0);

        Assert.Equal("Waiting", label);
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

    [Fact]
    public void ResolveStateLabel_ExplicitError_ReturnsError()
    {
        var resolver = new PresenceStatusLabelResolver();
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.Ready,
                IsError = true
            });

        var label = resolver.ResolveStateLabel(new PresenceTemplateOptions(), context, CodexActivityKind.Ready, 0);

        Assert.Equal("Error", label);
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
