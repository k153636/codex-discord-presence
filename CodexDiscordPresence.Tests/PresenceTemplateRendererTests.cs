using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceTemplateRendererTests
{
    [Fact]
    public void Render_WithoutRecentEditedFile_UsesAnalyzingProjectActivity()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Thinking", presence.State);
    }

    [Fact]
    public void Render_ReadyWithoutRecentEditedFile_UsesReadyActivity()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Idling", presence.State);
    }

    [Fact]
    public void Render_ReadyWithinFiveMinutes_UsesWaitingLabel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 0, null),
            sessionAge: TimeSpan.FromMinutes(4));

        var presence = renderer.Render(template, context);

        Assert.Equal("Waiting", presence.State);
    }

    [Fact]
    public void Render_AnalyzingProjectWithTaskStart_UsesWorkingLabel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                LastTaskStartedAt = now
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Working", presence.State);
    }

    [Fact]
    public void Render_RunningCommandWithTaskStart_StillUsesRunningCommandLabel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var now = DateTime.UtcNow;
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                DetectedActivityKind = CodexActivityKind.RunningCommand,
                LastTaskStartedAt = now
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Running command", presence.State);
    }

    [Fact]
    public void Render_DefaultPresence_UsesModel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions
        {
            Details = "{GoalModePrefix} {ModelName} • {Tokens}",
            State = "{ActivityLine}",
            LargeImageText = "working on {ProjectName}",
            SmallImageText = "{ProjectFileCount} files • session {SessionElapsed}"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                CollaborationMode = "goal"
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Plan mode: gpt-5-codex • Tokens pending", presence.Details);
        Assert.Equal("Thinking", presence.State);
        Assert.Equal("working on Nexstrap", presence.LargeImageText);
        Assert.Equal("128 files • session 5m", presence.SmallImageText);
    }

    [Fact]
    public void Render_Details_UsesPlanModePrefix()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions
        {
            Details = "{GoalModePrefix} {ModelName} • {Tokens}"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                CollaborationMode = "plan"
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Plan mode: gpt-5-codex • Tokens pending", presence.Details);
    }

    [Fact]
    public void Render_Details_OmitsModePrefixWhenModeIsOther()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions
        {
            Details = "{GoalModePrefix} {ModelName} • {Tokens}"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                CollaborationMode = "draft"
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("gpt-5-codex • Tokens pending", presence.Details);
    }

    [Fact]
    public void Render_Details_UsesCodeModePrefixDuringImplementation()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions
        {
            Details = "{GoalModePrefix} {ModelName} • {Tokens}"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                CollaborationMode = "plan",
                DetectedActivityKind = CodexActivityKind.ApplyingEdits
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Code mode: gpt-5-codex • Tokens pending", presence.Details);
    }

    [Fact]
    public void Render_ThinkingElapsed_UsesActivityStartTime()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var startedAt = DateTime.UtcNow.AddSeconds(-19);
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                ActivityStartedAt = startedAt,
                LastObservedAt = DateTime.UtcNow
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null),
            lastObservedAt: DateTime.UtcNow);

        var presence = renderer.Render(template, context);

        Assert.Equal("Thinking", presence.State);
    }

    [Fact]
    public void Render_ThinkingJustStarted_OmitsZeroElapsedSuffix()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                ActivityStartedAt = DateTime.UtcNow,
                LastObservedAt = DateTime.UtcNow
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null),
            lastObservedAt: DateTime.UtcNow);

        var presence = renderer.Render(template, context);

        Assert.Equal("Thinking", presence.State);
    }

    [Fact]
    public void Render_AnalyzingProjectRepeatCount_AppendsX2()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true)
            {
                ActivityRepeatCount = 2
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Thinking x2", presence.State);
    }

    [Fact]
    public void Render_WithRecentEditedFile_UsesApplyingEditsActivity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "CodexRpcRendererTest.txt");
        File.WriteAllText(tempFile, "test");

        try
        {
            var renderer = new PresenceTemplateRenderer();
            var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var observedAt = DateTime.UtcNow.AddSeconds(-12);
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.ApplyingEdits,
                    ActivityProvenance = ActivityProvenance.Observed,
                    LastObservedAt = observedAt
                },
                new ProjectSnapshot(
                    "Nexstrap",
                    Path.GetTempPath(),
                    Path.GetFileName(tempFile),
                    tempFile,
                    1,
                    1,
                    42000,
                    [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]),
                new GitSnapshot(true, 2, null),
                sessionAge: TimeSpan.FromSeconds(12),
                lastObservedAt: observedAt,
                recentEditedFiles: [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]);

            var presence = renderer.Render(template, context);

            Assert.Equal("Applying edits • CodexRpcRendererTest.txt", presence.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Render_WithStaleEditedFile_FallsBackToReadyActivity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "CodexRpcRendererStaleTest.txt");
        File.WriteAllText(tempFile, "test");
        File.SetLastWriteTimeUtc(tempFile, DateTime.UtcNow.AddMinutes(-5));

        try
        {
            var renderer = new PresenceTemplateRenderer();
            var template = new PresenceTemplateOptions { State = "{ActivityLine}", EditingFreshnessSeconds = 45 };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false),
                new ProjectSnapshot(
                    "Nexstrap",
                    Path.GetTempPath(),
                    Path.GetFileName(tempFile),
                    tempFile,
                    1,
                    1,
                    42000,
                    []),
                new GitSnapshot(true, 2, null),
                recentEditedFiles: []);

            var presence = renderer.Render(template, context);

            Assert.Equal("Idling", presence.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Render_EditingFileName_IsRawFileName()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "CodexRpcRendererRawNameTest.txt");
        File.WriteAllText(tempFile, "test");

        try
        {
            var renderer = new PresenceTemplateRenderer();
            var template = new PresenceTemplateOptions { State = "Editing {EditingFileName}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false),
                new ProjectSnapshot(
                    "Nexstrap",
                    Path.GetTempPath(),
                    Path.GetFileName(tempFile),
                    tempFile,
                    128,
                    128,
                    42000,
                    []),
                new GitSnapshot(true, 1, null),
                recentEditedFiles: [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]);

            var presence = renderer.Render(template, context);

            Assert.Equal("Editing CodexRpcRendererRawNameTest.txt", presence.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Render_ProjectSizeText_FormatsFilesAndLines()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { LargeImageText = "{ProjectSizeText}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 1532, 1532, 142_400, []),
            new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("1.5K files • 142.4K lines", presence.LargeImageText);
    }

    [Fact]
    public void Render_LargeImageText_UsesProjectFileCount()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { LargeImageText = "{ProjectFileCount} files" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 1532, 1532, 142_400, []),
            new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("1532 files", presence.LargeImageText);
    }

    [Fact]
    public void Render_WithTwoRecentEditedFiles_UsesCoordinatingChangesActivity()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.CoordinatingChanges,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now
            },
            new ProjectSnapshot(
                "Nexstrap",
                @"E:\tool\Nexstrap",
                "Program.cs",
                @"E:\tool\Nexstrap\Program.cs",
                2,
                2,
                42000,
                []),
            new GitSnapshot(true, 2, null),
            sessionAge: TimeSpan.FromSeconds(12),
            lastObservedAt: now,
            recentEditedFiles:
            [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Coordinating changes across 2 files", presence.State);
    }

    [Fact]
    public void Render_WithCreatedFilesActivity_UsesCreatingFilesLabel()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.CreatingFiles,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now
            },
            new ProjectSnapshot(
                "Nexstrap",
                @"E:\tool\Nexstrap",
                "NewFile.cs",
                @"E:\tool\Nexstrap\NewFile.cs",
                1,
                1,
                42000,
                []),
            new GitSnapshot(true, 1, null, CreatedFileCount: 1),
            sessionAge: TimeSpan.FromSeconds(12),
            lastObservedAt: now,
            recentEditedFiles: [new RecentProjectFileSnapshot("NewFile.cs", @"E:\tool\Nexstrap\NewFile.cs", now)]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Creating files • NewFile.cs", presence.State);
    }

    [Fact]
    public void Render_WithDeletedFilesActivity_UsesDeletingFilesLabel()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.DeletingFiles,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now
            },
            new ProjectSnapshot(
                "Nexstrap",
                @"E:\tool\Nexstrap",
                null,
                null,
                0,
                0,
                42000,
                []),
            new GitSnapshot(true, 1, null, DeletedFileCount: 1),
            sessionAge: TimeSpan.FromSeconds(12),
            lastObservedAt: now);

        var presence = renderer.Render(template, context);

        Assert.Equal("Deleting files", presence.State);
    }

    [Fact]
    public void Render_ActiveEditedFilesText_SuppressesMultiFileRedundancy()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActiveEditedFilesText}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.CoordinatingChanges,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now
            },
            new ProjectSnapshot(
                "Nexstrap",
                @"E:\tool\Nexstrap",
                "Program.cs",
                @"E:\tool\Nexstrap\Program.cs",
                2,
                2,
                42000,
                []),
            new GitSnapshot(true, 2, null),
            lastObservedAt: now,
            recentEditedFiles:
            [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-1))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("", presence.State);
    }

    [Fact]
    public void Render_WithRunningCommand_UsesRunningCommandActivity()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.RunningCommand,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now
            },
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 0, null),
            sessionAge: TimeSpan.FromSeconds(12),
            lastObservedAt: now);

        var presence = renderer.Render(template, context);

        Assert.Equal("Running command", presence.State);
    }

    [Fact]
    public void Render_WithRefactoringActivity_UsesRefactoringLabel()
    {
        var now = DateTime.UtcNow.AddSeconds(-12);
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.Refactoring,
                ActivityProvenance = ActivityProvenance.Observed,
                Confidence = ActivityConfidence.Low,
                LastObservedAt = now
            },
            new ProjectSnapshot(
                "Nexstrap",
                @"E:\tool\Nexstrap",
                "Program.cs",
                @"E:\tool\Nexstrap\Program.cs",
                4,
                4,
                42000,
                []),
            new GitSnapshot(true, 4, "refactor: split editor state from transport"),
            sessionAge: TimeSpan.FromSeconds(12),
            lastObservedAt: now,
            recentEditedFiles:
            [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5)),
                new RecentProjectFileSnapshot("PresenceTemplateRenderer.cs", @"E:\tool\Nexstrap\PresenceTemplateRenderer.cs", now.AddSeconds(-10)),
                new RecentProjectFileSnapshot("ProjectInspector.cs", @"E:\tool\Nexstrap\ProjectInspector.cs", now.AddSeconds(-15))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Refactoring", presence.State);
    }

    [Fact]
    public void Render_SessionElapsed_UsesMinuteFloorWhenUnderOneMinute()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{SessionElapsed}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null),
            sessionAge: TimeSpan.FromSeconds(3));

        var presence = renderer.Render(template, context);

        Assert.Equal("1m", presence.State);
    }

    private static PresenceContext CreateContext(
        CodexProcessSnapshot codex,
        ProjectSnapshot project,
        GitSnapshot git,
        TimeSpan? sessionAge = null,
        DateTime? lastObservedAt = null,
        DateTime? lastTaskStartedAt = null,
        IReadOnlyList<RecentProjectFileSnapshot>? recentEditedFiles = null)
    {
        var startedAt = DateTime.UtcNow - (sessionAge ?? TimeSpan.FromMinutes(5));
        var snapshot = codex with
        {
            RecentEditedFiles = recentEditedFiles ?? Array.Empty<RecentProjectFileSnapshot>(),
            LastObservedAt = lastObservedAt
        };

        if (lastTaskStartedAt.HasValue)
        {
            snapshot = snapshot with
            {
                LastTaskStartedAt = lastTaskStartedAt
            };
        }

        return new PresenceContext(
            "gpt-5-codex",
            snapshot,
            project,
            git,
            new SessionSnapshot(startedAt, DateTime.UtcNow - startedAt),
            new TokenUsageSnapshot(null, null));
    }
}


