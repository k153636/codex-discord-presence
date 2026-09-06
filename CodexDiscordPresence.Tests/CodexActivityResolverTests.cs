using System;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexActivityResolverTests
{
    [Fact]
    public void Resolve_TaskCompleteWinsOverStaleEditEvidence()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(
                true,
                true,
                true,
                true,
                now.AddSeconds(-10),
                now.AddSeconds(-1),
                now,
                null,
                false,
                null,
                null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.ApplyingEdits,
            [new RecentProjectFileSnapshot("Completed.cs", @"E:\tool\Completed.cs", now)],
            changedFileCount: 1);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Ready, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Equal("task_complete without newer task_started", reason);
    }

    [Fact]
    public void Resolve_NewTaskStartedAfterCompletionDoesNotInventEditing()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(
                true,
                true,
                true,
                true,
                now,
                now.AddSeconds(-1),
                now,
                null,
                false,
                null,
                null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.AnalyzingProject,
            changedFileCount: 1);

        var activity = resolver.Resolve(context, out _, out _, out var reason, out _);

        Assert.Equal(CodexActivityKind.AnalyzingProject, activity);
        Assert.Contains("task_started", reason);
    }

    [Fact]
    public void Resolve_TaskStartedWithDiffWithoutToolEventRemainsAnalyzing()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out var lastObservedAt);

        Assert.Equal(CodexActivityKind.AnalyzingProject, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
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
    public void Resolve_CompletedEditEventFallsBackAfterPropagationGrace()
    {
        var now = DateTime.UtcNow;
        var filePath = @"E:\repo\Completed.cs";
        var resolver = new CodexActivityResolver(
            new CodexActivityStateMachine(),
            () => now);
        var sessionInspection = new SessionInspection(
            true,
            true,
            true,
            false,
            now.AddSeconds(-5),
            null,
            now,
            null,
            false,
            null,
            null)
        {
            ActivityEvents =
            [
                new CodexActivityEvent
                {
                    Sequence = 1,
                    TimestampUtc = now.AddSeconds(-5),
                    Kind = CodexActivityEventKind.TurnStarted,
                    TurnId = "turn-1",
                    Reason = "task_started"
                },
                new CodexActivityEvent
                {
                    Sequence = 2,
                    TimestampUtc = now.AddSeconds(-4),
                    Kind = CodexActivityEventKind.OperationStarted,
                    TurnId = "turn-1",
                    CallId = "call-1",
                    OperationKind = CodexOperationKind.Edit,
                    TargetPaths = [filePath],
                    Reason = "edit operation started"
                },
                new CodexActivityEvent
                {
                    Sequence = 3,
                    TimestampUtc = now.AddSeconds(-3),
                    Kind = CodexActivityEventKind.OperationCompleted,
                    TurnId = "turn-1",
                    CallId = "call-1",
                    Reason = "tool operation completed"
                }
            ]
        };
        var context = CreateContext(
            sessionInspection,
            new GitSnapshot(true, 1, null),
            CodexActivityKind.ApplyingEdits,
            [new RecentProjectFileSnapshot("Completed.cs", filePath, now)],
            changedFileCount: 1);

        var activity = resolver.Resolve(context, out var provenance, out _, out var reason, out _);

        Assert.Equal(CodexActivityKind.AnalyzingProject, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Contains("without a pending operation", reason);
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
    public void Resolve_GitShellCommand_ReturnsRunningCommand()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, true, "shell_command looks like git", null)
            {
                LastRunningCommandKind = RunningCommandKind.Git
            },
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.RunningCommand, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("git", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_TestShellCommand_ReturnsRunningCommand()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, true, "shell_command looks like test", null)
            {
                LastRunningCommandKind = RunningCommandKind.Test
            },
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.RunningCommand, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("test", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_SearchShellCommand_ReturnsRunningCommand()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, true, "shell_command looks like search", null)
            {
                LastRunningCommandKind = RunningCommandKind.Search,
                LastShellCommandWasInvestigative = true
            },
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.RunningCommand, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("search", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_BuildShellCommand_ReturnsRunningCommand()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, true, "shell_command looks like build", null)
            {
                LastRunningCommandKind = RunningCommandKind.Build
            },
            new GitSnapshot(true, 0, null),
            CodexActivityKind.AnalyzingProject);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.RunningCommand, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("build", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_StaleCreatedFilesFallsBackToReady()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now.AddMinutes(-30), null, now.AddMinutes(-30), null, false, null, null),
            new GitSnapshot(true, 1, null, CreatedFileCount: 1),
            null,
            [],
            changedFileCount: 1,
            thinkingStaleTimeoutMinutes: 10);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Ready, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Equal("Codex running but idle", reason);
    }

    [Fact]
    public void Resolve_FreshSessionWithoutFreshEditsDoesNotKeepCreatingFiles()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var recentEditedFiles = new[]
        {
            new RecentProjectFileSnapshot("Created.cs", @"E:\tool\Created.cs", now.AddMinutes(-30))
        };
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now.AddMinutes(-30), null, now, null, false, null, null),
            new GitSnapshot(true, 1, null, CreatedFileCount: 1),
            CodexActivityKind.AnalyzingProject,
            recentEditedFiles,
            changedFileCount: 1,
            thinkingStaleTimeoutMinutes: 10,
            editingFreshnessSeconds: 12);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.Ready, activity);
        Assert.Equal(ActivityProvenance.Inferred, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Equal("Codex running but idle", reason);
    }

    [Fact]
    public void Resolve_SingleCreatedFileWithFreshEdit_ReturnsCreatingFiles()
    {
        var resolver = new CodexActivityResolver();
        var now = DateTime.UtcNow;
        var recentEditedFiles = new[]
        {
            new RecentProjectFileSnapshot("Created.cs", @"E:\tool\Created.cs", now)
        };
        var context = CreateContext(
            new SessionInspection(true, true, true, false, now, null, now, null, false, null, null),
            new GitSnapshot(true, 1, null, CreatedFileCount: 1),
            CodexActivityKind.AnalyzingProject,
            recentEditedFiles,
            changedFileCount: 1,
            thinkingStaleTimeoutMinutes: 10,
            editingFreshnessSeconds: 120);

        var activity = resolver.Resolve(context, out var provenance, out var confidence, out var reason, out _);

        Assert.Equal(CodexActivityKind.CreatingFiles, activity);
        Assert.Equal(ActivityProvenance.Observed, provenance);
        Assert.Equal(ActivityConfidence.High, confidence);
        Assert.Contains("created file=Created.cs", reason);
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
