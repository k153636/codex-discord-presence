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
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 42000, []),
            new GitSnapshot(true, 1, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Analyzing project", presence.State);
    }

    [Fact]
    public void Render_ReadyWithoutRecentEditedFile_UsesReadyActivity()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 42000, []),
            new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Ready", presence.State);
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
                    128,
                    42000,
                    [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]),
                new GitSnapshot(true, 2, null));

            var presence = renderer.Render(template, context);

            Assert.Equal("Applying edits ・ Editing CodexRpcRendererTest.txt", presence.State);
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
                    128,
                    42000,
                    [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]),
                new GitSnapshot(true, 2, null));

            var presence = renderer.Render(template, context);

            Assert.Equal("Ready", presence.State);
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
                    42000,
                    [new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, File.GetLastWriteTimeUtc(tempFile))]),
                new GitSnapshot(true, 1, null));

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
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 1532, 142_400, []),
            new GitSnapshot(true, 0, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("1.5k files ・ 142.4k lines", presence.LargeImageText);
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
                128,
                42000,
                [
                    new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                    new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5))
                ]),
            new GitSnapshot(true, 2, null));

        var presence = renderer.Render(template, context);

        Assert.Equal("Coordinating changes across 2 files", presence.State);
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
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null, 128, 42000, []),
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
                128,
                42000,
                [
                    new RecentProjectFileSnapshot("Program.cs", @"E:\tool\Nexstrap\Program.cs", now),
                    new RecentProjectFileSnapshot("AppOptions.cs", @"E:\tool\Nexstrap\AppOptions.cs", now.AddSeconds(-5)),
                    new RecentProjectFileSnapshot("PresenceTemplateRenderer.cs", @"E:\tool\Nexstrap\PresenceTemplateRenderer.cs", now.AddSeconds(-10)),
                    new RecentProjectFileSnapshot("ProjectInspector.cs", @"E:\tool\Nexstrap\ProjectInspector.cs", now.AddSeconds(-15))
                ]),
            new GitSnapshot(true, 4, "refactor: split editor state from transport"));

        var presence = renderer.Render(template, context);

        Assert.Equal("Refactoring", presence.State);
    }

    private static PresenceContext CreateContext(
        CodexProcessSnapshot codex,
        ProjectSnapshot project,
        GitSnapshot git)
    {
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        return new PresenceContext(
            "gpt-5-codex",
            codex,
            project,
            git,
            new SessionSnapshot(startedAt, DateTime.UtcNow - startedAt),
            new TokenUsageSnapshot(null, null));
    }
}
