using System.Text;
using System.Text.Json;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class AntigravityStatusLinePayloadParserTests
{
    [Fact]
    public void TryParse_OfficialPayload_ProducesSafeObservation()
    {
        const string payload = """
            {
              "cwd": "C:\\Users\\redacted\\my-project",
              "session_id": "legacy-session-id",
              "conversation_id": "conversation-id",
              "transcript_path": "C:\\Users\\redacted\\.gemini\\transcript.jsonl",
              "model": {
                "id": "gemini-3.5-flash-high",
                "display_name": "Gemini 3.5 Flash (High)"
              },
              "workspace": {
                "current_dir": "C:\\Users\\redacted\\my-project",
                "project_dir": "C:\\Users\\redacted\\my-project"
              },
              "version": "1.0.13",
              "agent_state": "working",
              "product": "antigravity",
              "email": "redacted@example.invalid",
              "execution_mode": "planning",
              "context_window": {
                "total_input_tokens": 88244,
                "total_output_tokens": 61074
              }
            }
            """;
        var observedAt = new DateTimeOffset(2026, 9, 11, 4, 5, 6, TimeSpan.FromHours(9));
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(Encoding.UTF8.GetBytes(payload), observedAt, out var observation);

        Assert.True(parsed);
        var result = Assert.IsType<ProviderObservation>(observation);
        Assert.Equal(ProviderObservationSource.AntigravityCli, result.Source);
        Assert.Equal(observedAt.ToUniversalTime(), result.ObservedAtUtc);
        Assert.Equal(ProviderAgentState.Working, result.AgentState);
        Assert.Equal("conversation-id", result.ConversationId);
        Assert.Equal("gemini-3.5-flash-high", result.Model?.Id);
        Assert.Equal("Gemini 3.5 Flash (High)", result.Model?.DisplayName);
        var workspace = Assert.IsType<ProviderWorkspaceObservation>(result.Workspace);
        Assert.Equal("my-project", workspace.WorkspaceName);
        Assert.Equal("my-project", workspace.ProjectName);
        Assert.True(workspace.MatchesProjectPath(@"C:\Users\redacted\my-project"));
        Assert.False(workspace.MatchesProjectPath(@"C:\Other\my-project"));

        var serialized = JsonSerializer.Serialize(result);
        Assert.DoesNotContain("redacted@example.invalid", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript.jsonl", serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(@"C:\Users\redacted", serialized, StringComparison.Ordinal);
    }

    [Fact]
    public void TryParse_MissingConversationId_UsesSessionIdAlias()
    {
        const string payload = """
            {
              "session_id": "session-alias",
              "agent_state": "idle"
            }
            """;
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.True(parsed);
        Assert.Equal("session-alias", observation?.ConversationId);
        Assert.Equal(ProviderAgentState.Idle, observation?.AgentState);
    }

    [Fact]
    public void TryParse_UnknownAgentState_MapsToUnknown()
    {
        const string payload = "{\"agent_state\":\"future_state\",\"model\":{\"id\":\"m\"}}";
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.True(parsed);
        Assert.Equal(ProviderAgentState.Unknown, observation?.AgentState);
        Assert.Equal("m", observation?.Model?.Id);
    }

    [Fact]
    public void TryParse_MissingProjectDirectory_UsesCwdForProjectMatching()
    {
        const string payload = """
            {
              "cwd": "C:\\repo\\current-project",
              "workspace": {
                "current_dir": "C:\\repo\\current-project"
              }
            }
            """;
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.True(parsed);
        var workspace = Assert.IsType<ProviderWorkspaceObservation>(observation?.Workspace);
        Assert.True(workspace.MatchesProjectPath(@"C:\repo\current-project"));
        Assert.False(workspace.MatchesProjectPath(@"C:\other\current-project"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("\"status\"")]
    public void TryParse_InvalidOrUnexpectedJson_ReturnsFalse(string payload)
    {
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(
            Encoding.UTF8.GetBytes(payload),
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.False(parsed);
        Assert.Null(observation);
    }

    [Fact]
    public void TryParse_OverLimitPayload_ReturnsFalse()
    {
        var payload = Encoding.UTF8.GetBytes(
             "{\"model\":{\"display_name\":\"" +
             new string('x', AntigravityStatusLinePayloadParser.MaxPayloadBytes) +
             "\"}}");
        var parser = new AntigravityStatusLinePayloadParser();

        var parsed = parser.TryParse(
            payload,
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.False(parsed);
        Assert.Null(observation);
    }

    [Fact]
    public void CommandContract_SeparatesStatusLineIoFromObservationParsing()
    {
        Assert.Equal("~/.gemini/antigravity-cli/settings.json", AntigravityStatusLineCommandContract.SettingsPath);
        Assert.Equal("statusLine", AntigravityStatusLineCommandContract.SettingsPropertyName);
        Assert.Equal("command", AntigravityStatusLineCommandContract.CommandType);
        Assert.Contains("stdin", AntigravityStatusLineCommandContract.StandardInput, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("stdout", AntigravityStatusLineCommandContract.StandardOutput, StringComparison.OrdinalIgnoreCase);
    }
}
