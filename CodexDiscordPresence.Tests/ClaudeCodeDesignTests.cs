using System.Text.Json;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeDesignTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly string Project = Path.GetFullPath(Path.GetTempPath());

    [Theory]
    [InlineData("Artifact", "{\"action\":\"quickstart\",\"intent\":\"design\"}", true)]
    [InlineData("Artifact", "{\"file_path\":\"C:/private/canvas.dc.html\"}", true)]
    [InlineData("Artifact", "{\"action\":\"quickstart\",\"intent\":\"slides\"}", false)]
    [InlineData("Artifact", "{\"file_path\":\"design.html\"}", false)]
    [InlineData("Skill", "{\"skill\":\"frontend-design\"}", false)]
    [InlineData("Write", "{\"file_path\":\"canvas.dc.html\"}", false)]
    [InlineData("mcp__claude_ai_Figma__search_design_system", "{}", false)]
    [InlineData("ToolSearch", "{\"query\":\"Artifact design\"}", false)]
    [InlineData("Artifact", "null", false)]
    public void Parse_ExecutionEvidence_RejectsGenericDesignMentions(string tool, string input, bool expected)
    {
        var hook = Parse(tool, input);
        Assert.Equal(expected, hook.IsClaudeDesignOperation);
        Assert.DoesNotContain("private", JsonSerializer.Serialize(hook));
    }

    [Theory]
    [InlineData("UserPromptSubmit")]
    [InlineData("Stop")]
    [InlineData("SessionEnd")]
    [InlineData("SessionStart")]
    public void Apply_ConfirmedDesign_ClearsAtTurnAndSessionBoundaries(string boundary)
    {
        var state = ConfirmedDesign();
        Assert.True(state.UsesClaudeDesign);
        state = ClaudeCodeSessionObservation.Apply(state, new("main", Project, boundary, Now));
        Assert.False(state.UsesClaudeDesign);
        state = ClaudeCodeSessionObservation.Apply(ConfirmedDesign(), new("different", Project, "UserPromptSubmit", Now));
        Assert.False(state.UsesClaudeDesign);
    }

    [Fact]
    public void Apply_FailedDesignCall_RemovesPendingEvidenceButPreservesOtherConfirmedUsage()
    {
        var started = ClaudeCodeSessionObservation.Apply(null, Parse("Artifact", "{\"action\":\"quickstart\",\"intent\":\"design\"}"));
        Assert.True(started.UsesClaudeDesign);
        var failed = ClaudeCodeSessionObservation.Apply(started, new("main", Project, "PostToolUseFailure", Now, ToolUseId: "tool"));
        Assert.False(failed.UsesClaudeDesign);
        var confirmed = ClaudeCodeSessionObservation.Apply(ConfirmedDesign(), new("main", Project, "PostToolUseFailure", Now, ToolUseId: "other"));
        Assert.True(confirmed.UsesClaudeDesign);
    }

    [Fact]
    public void Render_DesignMetadata_PreservesMcpActivityAndDoesNotLeakToAnotherProvider()
    {
        var state = ClaudeCodeSessionObservation.Apply(ConfirmedDesign(),
            new("main", Project, "PreToolUse", Now, "mcp__blender__inspect", "mcp"));
        var context = ClaudeCodePresenceProjection.CreateContext(state,
            new("project", Project, null, null, 0, 0, 0, []), new(false, 0, null), new(Now.UtcDateTime, TimeSpan.Zero));
        var renderer = new PresenceTemplateRenderer();
        var options = new PresenceTemplateOptions { State = "{ActivityLine}" };
        var presence = renderer.Render(options, context);
        Assert.StartsWith("Claude Design • ", presence.Details);
        Assert.Equal("MCP blender", presence.State);
        var ordinaryContext = new PresenceContext("Codex", context.Activity, context.Project, context.Git, context.Session, context.TokenUsage);
        var ordinary = renderer.Render(options, ordinaryContext);
        Assert.DoesNotContain("Claude Design", ordinary.Details);
        Assert.DoesNotContain("{FeatureLabel}", ordinary.Details);
        Assert.False(ordinary.Details.StartsWith("•", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    public void Read_RealTranscriptToolShape_HandlesSuccessFailureAndSidechains(bool error, bool sidechain, bool expected)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".jsonl");
        try
        {
            var tool = JsonSerializer.Serialize(new
            {
                sessionId = "main", cwd = Project, timestamp = Now, type = "assistant", isSidechain = sidechain,
                message = new { content = new[] { new { type = "tool_use", id = "tool", name = "Artifact", input = new { action = "quickstart", intent = "design" } } } }
            });
            var result = JsonSerializer.Serialize(new
            {
                sessionId = "main", cwd = Project, timestamp = Now, type = "user", isSidechain = sidechain,
                message = new { content = new[] { new { type = "tool_result", tool_use_id = "tool", is_error = error, content = "private response" } } }
            });
            File.WriteAllLines(path, [tool, result]);
            var observation = ClaudeCodeTranscriptActivityReader.Read(path);
            Assert.Equal(expected, observation?.UsesClaudeDesign ?? false);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ClaudeCodeHookEvent Parse(string tool, string input) => ClaudeCodeHookParser.Parse(
        JsonSerializer.Serialize(new
        {
            session_id = "main", cwd = Project, hook_event_name = "PreToolUse", tool_name = tool, tool_use_id = "tool",
            prompt = "please use Claude Design", tool_input = JsonSerializer.Deserialize<JsonElement>(input)
        }), Now)!;

    private static ClaudeCodeSessionObservation ConfirmedDesign()
    {
        var state = ClaudeCodeSessionObservation.Apply(null, Parse("Artifact", "{\"action\":\"quickstart\",\"intent\":\"design\"}"));
        return ClaudeCodeSessionObservation.Apply(state, new("main", Project, "PostToolUse", Now, ToolUseId: "tool"));
    }
}
