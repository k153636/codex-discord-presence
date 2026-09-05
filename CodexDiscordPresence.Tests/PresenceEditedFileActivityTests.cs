using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceEditedFileActivityTests
{
    [Fact]
    public void Render_SingleEditedFile_UsesEditingAndFileName()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var file = CreateFile(projectPath, "FileName.cs", now);

        var presence = Render(CodexActivityKind.ApplyingEdits, projectPath, [file]);

        Assert.Equal("Editing FileName.cs", presence.State);
    }

    [Fact]
    public void Render_TwoEditedFiles_UsesMostRecentlyActiveFileOnly()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var activeFile = CreateFile(projectPath, "Active.cs", now);
        var otherFile = CreateFile(projectPath, "Other.cs", now.AddSeconds(-1));

        var presence = Render(CodexActivityKind.ApplyingEdits, projectPath, [otherFile, activeFile]);

        Assert.Equal("Editing Active.cs", presence.State);
        Assert.DoesNotContain("Other.cs", presence.State, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_FourEditedFiles_UsesActiveFileAndRemainingCount()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var files = new[]
        {
            CreateFile(projectPath, "Active.cs", now),
            CreateFile(projectPath, "Second.cs", now.AddSeconds(-1)),
            CreateFile(projectPath, "Third.cs", now.AddSeconds(-2)),
            CreateFile(projectPath, "Fourth.cs", now.AddSeconds(-3))
        };

        var presence = Render(CodexActivityKind.ApplyingEdits, projectPath, files);

        Assert.Equal("Editing Active.cs + 3 files", presence.State);
    }

    [Fact]
    public void Render_CoordinatingChanges_UsesChangedFileCountWithoutFileName()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var files = new[]
        {
            CreateFile(projectPath, "Active.cs", now),
            CreateFile(projectPath, "Other.cs", now.AddSeconds(-1))
        };

        var presence = Render(CodexActivityKind.CoordinatingChanges, projectPath, files, gitChangedFileCount: 18);

        Assert.Equal("Coordinating 18 files", presence.State);
        Assert.DoesNotContain("Active.cs", presence.State, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_DirectToolTarget_TakesPriorityOverRecentWriteTime()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var recentFile = CreateFile(projectPath, "Recent.cs", now);
        var directFilePath = Path.Combine(projectPath, "src", "Direct.cs");
        var context = CreateContext(
            CodexActivityKind.ApplyingEdits,
            projectPath,
            [recentFile],
            gitChangedFileCount: 2,
            directToolFilePath: directFilePath,
            directToolFileAt: now);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Editing src/Direct.cs", presence.State);
        Assert.DoesNotContain(projectPath, presence.State, StringComparison.Ordinal);
    }

    private static RenderedPresence Render(
        CodexActivityKind activityKind,
        string projectPath,
        IReadOnlyList<RecentProjectFileSnapshot> recentFiles,
        int gitChangedFileCount = 0)
    {
        return new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            CreateContext(activityKind, projectPath, recentFiles, gitChangedFileCount));
    }

    private static PresenceContext CreateContext(
        CodexActivityKind activityKind,
        string projectPath,
        IReadOnlyList<RecentProjectFileSnapshot> recentFiles,
        int gitChangedFileCount = 0,
        string? directToolFilePath = null,
        DateTime? directToolFileAt = null)
    {
        var now = DateTime.UtcNow;
        return new PresenceContext(
            "gpt-5-codex",
            new CodexProcessSnapshot(true, "codex", false)
            {
                DetectedActivityKind = activityKind,
                ActivityProvenance = ActivityProvenance.Observed,
                LastObservedAt = now,
                LastDirectToolFilePath = directToolFilePath,
                LastDirectToolFileAt = directToolFileAt,
                RecentEditedFiles = recentFiles
            },
            new ProjectSnapshot(
                "PresenceDisplayProject",
                projectPath,
                recentFiles.FirstOrDefault()?.Name,
                recentFiles.FirstOrDefault()?.Path,
                recentFiles.Count,
                recentFiles.Count,
                100,
                recentFiles),
            new GitSnapshot(true, gitChangedFileCount, null),
            new SessionSnapshot(now.AddMinutes(-1), TimeSpan.FromMinutes(1)),
            new TokenUsageSnapshot(null, null));
    }

    private static string CreateProjectPath()
    {
        return Path.Combine(Path.GetTempPath(), "CodexPresenceDisplayProject_" + Guid.NewGuid());
    }

    private static RecentProjectFileSnapshot CreateFile(string projectPath, string name, DateTime lastWriteTimeUtc)
    {
        return new RecentProjectFileSnapshot(name, Path.Combine(projectPath, name), lastWriteTimeUtc);
    }
}
