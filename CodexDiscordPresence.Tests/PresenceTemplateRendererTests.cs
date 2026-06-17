using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceTemplateRendererTests
{
    [Fact]
    public void Render_WithoutRecentEditedFile_UsesProjectFallbackActivity()
    {
        var renderer = new PresenceTemplateRenderer();
        var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var context = CreateContext(
            new CodexProcessSnapshot(true, "codex", true),
            new ProjectSnapshot("Nexstrap", @"E:\tool\Nexstrap", null, null),
            new GitSnapshot(true, 1));

        var presence = renderer.Render(template, context);

        Assert.Equal("Thinking on Nexstrap ・ 1 file changed", presence.State);
    }

    [Fact]
    public void Render_WithRecentEditedFile_UsesEditingActivity()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), "CodexRpcRendererTest.txt");
        File.WriteAllText(tempFile, "test");

        try
        {
            var renderer = new PresenceTemplateRenderer();
            var template = new PresenceTemplateOptions { State = "{ActivityLine}" };
            var context = CreateContext(
                new CodexProcessSnapshot(true, "codex", false),
                new ProjectSnapshot("Nexstrap", Path.GetTempPath(), Path.GetFileName(tempFile), tempFile),
                new GitSnapshot(true, 2));

            var presence = renderer.Render(template, context);

            Assert.Equal("Editing CodexRpcRendererTest.txt ・ 2 files changed", presence.State);
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
                new ProjectSnapshot("Nexstrap", Path.GetTempPath(), Path.GetFileName(tempFile), tempFile),
                new GitSnapshot(true, 1));

            var presence = renderer.Render(template, context);

            Assert.Equal("Editing CodexRpcRendererRawNameTest.txt", presence.State);
        }
        finally
        {
            File.Delete(tempFile);
        }
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
