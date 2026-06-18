using System;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexActivityResolverTests
{
    [Fact]
    public void Resolve_TaskStartedWithDiff_PrefersApplyingEdits()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out var lastObservedAt);

        Assert.Equal(CodexActivityKind.ApplyingEdits, activity);
        Assert.Equal(ActivityProvenance.Mixed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("task_started", reason);
        Assert.Equal(now, lastObservedAt);
    }

    [Fact]
    public void Resolve_MultiFileRecentEdits_ReturnsCoordinatingChanges()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 2, null),
            CodexActivityKind.AnalyzingProject,
            [
                new RecentProjectFileSnapshot("One.cs", @"E:\tool\One.cs", now),
                new RecentProjectFileSnapshot("Two.cs", @"E:\tool\Two.cs", now.AddSeconds(-1))
            ],
            changedFileCount: 2);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.CoordinatingChanges, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("recent edits", reason);
    }

    [Fact]
    public void Resolve_MultipleRecentEditedFiles_UsesCoordinatingChanges()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.AnalyzingProject,
            [
                new RecentProjectFileSnapshot("One.cs", @"E:\tool\One.cs", now),
                new RecentProjectFileSnapshot("Two.cs", @"E:\tool\Two.cs", now.AddSeconds(-1))
            ],
            changedFileCount: 1);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.CoordinatingChanges, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("recent edits", reason);
    }

    [Fact]
    public void Resolve_PreviousCoordinatingChanges_FallsBackToAnalyzingProject()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 3, null),
            CodexActivityKind.CoordinatingChanges,
            [],
            changedFileCount: 3);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.AnalyzingProject, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("task_started", reason);
    }

    [Fact]
    public void Resolve_PreviousApplyingEditsWithoutFreshEdits_FallsBackToAnalyzingProject()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.ApplyingEdits,
            [],
            changedFileCount: 1);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.AnalyzingProject, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("task_started", reason);
    }

    [Fact]
    public void Resolve_CommandInSession_ReturnsRunningCommand()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, true, "shell command", null),
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.RunningCommand, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Equal("shell command", reason);
    }

    [Fact]
    public void Resolve_PlanMode_ReturnsPlanning()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, "plan", false, null, null),
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Planning, activity);
        Assert.Equal(ActivityConfidence.Low, confidence);
        Assert.Equal("turn_context collaboration_mode=plan", reason);
        Assert.Equal(ActivityProvenance.Observed, provenance);
    }

    [Fact]
    public void Resolve_RefactorCommitMessage_ReturnsRefactoring()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, "refactor: split detector"),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Refactoring, activity);
        Assert.Equal(ActivityConfidence.Low, confidence);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Contains("refactor", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_NoEvidence_ReturnsReady()
    {
        var resolver = new CodexActivityResolver();
        var context = CreateContext(null, null, null, [], 0, 10, 120);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Ready, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Equal("Codex running but idle", reason);
    }

    private static CodexActivityContext CreateContext(
        SessionInspection? sessionInspection,
        GitSnapshot? gitSnapshot,
        CodexActivityKind? previousActivityKind,
        IReadOnlyList<RecentProjectFileSnapshot>? recentEditedFiles = null,
        int changedFileCount = 0,
        int thinkingStaleTimeoutMinutes = 10,
        int editingFreshnessSeconds = 120)
    {
        return new CodexActivityContext(
            recentEditedFiles ?? [],
            changedFileCount == 0 ? gitSnapshot?.ChangedFileCount ?? 0 : changedFileCount,
            sessionInspection,
            gitSnapshot,
            previousActivityKind,
            thinkingStaleTimeoutMinutes,
            editingFreshnessSeconds);
    }
}
