using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeSpinnerTests
{
    [Theory]
    [InlineData("✽ Herding… (29s · ↓ 1.9k tokens · thinking with high effort)", "Herding")]
    [InlineData("✻ Cerebrating... (1m 2s · thinking)", "Cerebrating")]
    [InlineData("✶ Determining… (2s · ↓ 10 tokens)", "Determining")]
    [InlineData("✽ Roosting… (29s · ↓ 1.9k tokens · thinking with high effort)", "Roosting")]
    [InlineData("› Herding", null)]
    [InlineData("✻ Brewed for 1m 2s · done 19:32", null)]
    [InlineData("The user says Herding… (2s)", null)]
    [InlineData("✽ Herding… (2s)\n✽ Cerebrating… (1s)", null)]
    [InlineData("✽ C:/private… (1s)", null)]
    public void ParseScreen_UsesOnlyUnambiguousSpinnerRows(string screen, string? expected)
    {
        Assert.Equal(expected, ClaudeCodeSpinnerLabel.ParseScreen(screen));
    }

    [Theory]
    [InlineData("UserPromptSubmit", null, "Herding")]
    [InlineData("PermissionRequest", null, "Waiting")]
    [InlineData("Stop", null, "Waiting")]
    [InlineData("PreToolUse", "mcp__blender__inspect", "MCP blender")]
    [InlineData("PreToolUse", "Edit", "Editing app.cs")]
    public void Render_SpinnerLabel_RespectsLifecycleAndToolPrecedence(string eventName, string? tool, string expected)
    {
        var now = DateTimeOffset.UtcNow;
        var observation = ClaudeCodeSessionObservation.Apply(null,
            new("main", "C:/project", eventName, now, tool, "id", "app.cs"));
        var context = ClaudeCodePresenceProjection.CreateContext(observation,
            new ProjectSnapshot("project", "C:/project", null, null, 0, 0, 0, []),
            new GitSnapshot(false, 0, null), new SessionSnapshot(now.UtcDateTime, TimeSpan.Zero), "Herding");
        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" }, context);
        Assert.Equal(expected, presence.State);
    }
}
