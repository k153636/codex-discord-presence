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
    public void Render_ReadyWithinFiveMinutes_UsesHoldOnLabel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 0, null),
            TimeSpan.FromMinutes(4));

        var presence = renderer.Render(template, context);

        Assert.Equal("Hold on", presence.State);
    }

    [Fact]
    public void Render_DefaultPresence_UsesModelAndActivityLine()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions
        {
            Details = "{ModelName} \u2022 {Tokens}",
            State = "{ActivityLine}",
            LargeImageText = "working on {ProjectName}",
            SmallImageText = "{ProjectFileCount} files \u2022 session {SessionElapsed}"
        };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("gpt-5-codex \u2022 Tokens pending", presence.Details);
        Assert.Equal("Thinking", presence.State);
        Assert.Equal("working on Nexstrap", presence.LargeImageText);
        Assert.Equal("128 files \u2022 session 5m", presence.SmallImageText);
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
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.ApplyingEdits,
                    ActivityProvenance = ActivityProvenance.Observed
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
                recentEditedFiles: [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]);

            var presence = renderer.Render(template, context);

            Assert.Equal("Applying edits \u2022 CodexRpcRendererTest.txt", presence.State);
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

        Assert.Equal("1.5K files \u2022 142.4K lines", presence.LargeImageText);
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
    public void Render_WithTwoRecentEditedFiles_UsesUpdatingFilesActivity()
    {
        var now = DateTime.UtcNow;
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.UpdatingFiles,
                    ActivityProvenance = ActivityProvenance.Observed
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
            recentEditedFiles: [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Coordinating changes across 2 files \u2022 Program.cs", presence.State);
    }

    [Fact]
    public void Render_WithCreatedFilesActivity_UsesCreatingFilesLabel()
    {
        var now = DateTime.UtcNow;
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.CreatingFiles,
                    ActivityProvenance = ActivityProvenance.Observed
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
            recentEditedFiles: [new RecentProjectFileSnapshot("NewFile.cs", @"E:\tool\Nexstrap\NewFile.cs", now)]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Creating files \u2022 NewFile.cs", presence.State);
    }

    [Fact]
    public void Render_WithDeletedFilesActivity_UsesDeletingFilesLabel()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = CodexActivityKind.DeletingFiles,
                ActivityProvenance = ActivityProvenance.Observed
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
            new GitSnapshot(true, 1, null, DeletedFileCount: 1));

        var presence = renderer.Render(template, context);

        Assert.Equal("Deleting files", presence.State);
    }

    [Fact]
    public void Render_ActiveEditedFilesText_SuppressesMultiFileRedundancy()
    {
        var now = DateTime.UtcNow;
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActiveEditedFilesText}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.UpdatingFiles,
                    ActivityProvenance = ActivityProvenance.Observed
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
            recentEditedFiles: [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-1))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("", presence.State);
    }

    [Fact]
    public void Render_WithRunningCommand_UsesRunningCommandActivity()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.RunningCommand,
                    ActivityProvenance = ActivityProvenance.Observed
            },
                new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 128, 42000, []),
                new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Running command", presence.State);
    }

    [Fact]
    public void Render_WithRefactoringActivity_UsesRefactoringLabel()
    {
        var now = DateTime.UtcNow;
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false)
                {
                    DetectedActivityKind = CodexActivityKind.Refactoring,
                    ActivityProvenance = ActivityProvenance.Observed,
                Confidence = ActivityConfidence.Low
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
            recentEditedFiles: [
                new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5)),
                new RecentProjectFileSnapshot("PresenceTemplateRenderer.cs", @"E:\tool\Nexstrap\PresenceTemplateRenderer.cs", now.AddSeconds(-10)),
                new RecentProjectFileSnapshot("ProjectInspector.cs", @"E:\tool\Nexstrap\ProjectInspector.cs", now.AddSeconds(-15))
            ]);

        var presence = renderer.Render(template, context);

        Assert.Equal("Refactoring", presence.State);
    }

    private static PresenceContext CreateContext(
        CodexProcessSnapshot codex,
        ProjectSnapshot project,
        GitSnapshot git,
        TimeSpan? sessionAge = null,
        IReadOnlyList<RecentProjectFileSnapshot>? recentEditedFiles = null)
    {
        var startedAt = DateTime.UtcNow - (sessionAge ?? TimeSpan.FromMinutes(5));
        return new PresenceContext(
            "gpt-5-codex",
            codex with
            {
                RecentEditedFiles = recentEditedFiles ?? Array.Empty<RecentProjectFileSnapshot>()
            },
            project,
            git,
            new SessionSnapshot(startedAt, DateTime.UtcNow - startedAt),
            new TokenUsageSnapshot(null, null));
    }
}

