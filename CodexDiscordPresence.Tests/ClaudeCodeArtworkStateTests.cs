using System.Text.Json;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeArtworkStateTests
{
    private static readonly DateTimeOffset Start = new(2026, 10, 9, 0, 0, 0, TimeSpan.Zero);
    private static readonly string Project = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "claude-artwork"));
    private readonly DiscordOptions _options = ClaudeCodeAssetPolicy.CreateDiscordOptions();
    private DateTimeOffset _now = Start;

    [Theory]
    [InlineData("Notification")]
    [InlineData("PermissionRequest")]
    [InlineData("Stop")]
    [InlineData("SessionStart")]
    public void Resolve_WaitingAndIdleUseSleepingWithoutPermanentNotification(string eventName)
    {
        var observation = Observe(null, eventName, Start);
        var rendered = Render(observation);
        Assert.Equal("claude_idle", DiscordAssetKeyResolver.ResolveLargeImageKey(_options, rendered));
        var payload = DiscordRichPresenceBuilder.Create(_options, rendered, "party");
        Assert.Null(payload.Assets.SmallImageKey);
        Assert.Null(payload.Assets.SmallImageText);
    }

    [Fact]
    public void Apply_InputSurvivesToolEventsAndRestoresLatestActivityAfterAcknowledgedLoop()
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        AssertNotification(artwork.Apply(observation, Render(observation), _options, null));
        _now = Start.AddSeconds(2);
        observation = Observe(observation, "PreToolUse", _now, "Read");
        var reading = Render(observation);
        AssertNotification(artwork.Apply(observation, reading, _options, null));
        _now = Start.AddSeconds(5);
        var acknowledged = Acknowledged(_now);
        AssertNotification(artwork.Apply(observation, reading, _options, acknowledged));
        _now = Start.AddSeconds(10.999);
        // Repeated responses do not prolong this input's animation window.
        AssertNotification(artwork.Apply(observation, reading, _options, Acknowledged(_now)));
        Assert.Equal(TimeSpan.FromMilliseconds(1), artwork.GetNextDelay(TimeSpan.FromSeconds(3)));
        _now = Start.AddSeconds(11);
        var restored = artwork.Apply(observation, reading, _options, Acknowledged(_now));
        Assert.Equal("claude_working", DiscordAssetKeyResolver.ResolveLargeImageKey(_options, restored));
        Assert.Equal(reading.State, restored.State);
        Assert.Equal(reading.Details, restored.Details);
        Assert.Equal(reading.ActivityKind, restored.ActivityKind);
        Assert.Equal(TimeSpan.FromSeconds(3), artwork.GetNextDelay(TimeSpan.FromSeconds(3)));
        _now = Start.AddSeconds(20);
        Assert.Null(artwork.Apply(observation, reading, _options, null).LargeImageKeyOverride);
    }

    [Theory]
    [InlineData("codex", "https://cdn.qualit.ly/clawd-notification.gif", 1)]
    [InlineData("claude-code", "https://cdn.qualit.ly/clawd-working-typing.gif", 1)]
    [InlineData("claude-code", "https://cdn.qualit.ly/clawd-notification.gif", -1)]
    [InlineData("claude-code", "https://cdn.qualit.ly/clawd-notification.gif", 50)]
    public void Apply_UnrelatedStaleOrFutureResponsesDoNotStartTheLoop(string provider, string image, int seconds)
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        var presence = Render(observation);
        artwork.Apply(observation, presence, _options, null);
        _now = Start.AddSeconds(29);
        AssertNotification(artwork.Apply(observation, presence, _options,
            Acknowledged(Start.AddSeconds(seconds)) with { ProviderId = provider, LargeImageKey = image }));
        _now = Start.AddSeconds(30);
        Assert.Null(artwork.Apply(observation, presence, _options, null).LargeImageKeyOverride);
    }

    [Fact]
    public void RestoreIfDue_UsesLatestActivityBeforeSlowEnrichmentAndDoesNotRestoreTwice()
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        artwork.Apply(observation, Render(observation), _options, null);
        _now = Start.AddSeconds(1);
        var acknowledged = Acknowledged(_now);
        Assert.Null(artwork.RestoreIfDue(_options, acknowledged));
        observation = Observe(observation, "PreToolUse", _now, "Read");
        artwork.Apply(observation, Render(observation), _options, acknowledged);
        _now = Start.AddSeconds(6.999);
        Assert.Null(artwork.RestoreIfDue(_options, acknowledged));
        _now = Start.AddSeconds(7);
        var restored = artwork.RestoreIfDue(_options, acknowledged)!;
        Assert.Equal(CodexActivityKind.ReadingFiles, restored.ActivityKind);
        Assert.Null(restored.LargeImageKeyOverride);
        Assert.Null(artwork.RestoreIfDue(_options, acknowledged));
    }

    [Theory]
    [InlineData("Notification")]
    [InlineData("PermissionRequest")]
    [InlineData("Stop")]
    [InlineData("SessionEnd")]
    [InlineData("PostToolUseFailure")]
    public void Apply_WaitingCompletionErrorOrSessionEndImmediatelyCancelsNotification(string eventName)
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        artwork.Apply(observation, Render(observation), _options, null);
        _now = Start.AddSeconds(1);
        observation = Observe(observation, eventName, _now);
        Assert.Null(artwork.Apply(observation, Render(observation), _options, Acknowledged(_now)).LargeImageKeyOverride);
    }

    [Fact]
    public void Apply_NewPromptRearmsButSubagentAndRepeatedObservationsDoNot()
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        artwork.Apply(observation, Render(observation), _options, Acknowledged(Start));
        _now = Start.AddSeconds(6);
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
        observation = Observe(observation, "SubagentStart", _now);
        Assert.Equal(Start, observation.LastUserPromptAtUtc);
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
        _now = Start.AddSeconds(7);
        observation = Observe(observation, "UserPromptSubmit", _now);
        Assert.Equal(_now, observation.LastUserPromptAtUtc);
        AssertNotification(artwork.Apply(observation, Render(observation), _options, null));
        artwork.Cancel();
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
    }

    [Theory]
    [InlineData(-31)]
    [InlineData(1)]
    public void Apply_PastSessionPromptsAndFuturePromptsDoNotReplay(int promptOffset)
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start.AddSeconds(promptOffset));
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
    }

    [Fact]
    public void Apply_NewSessionDoesNotInheritThePreviousSessionsNotification()
    {
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        artwork.Apply(observation, Render(observation), _options, null);
        observation = Observe(null, "SessionStart", Start) with { SessionId = "other" };
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
    }

    [Fact]
    public void Apply_ConfiguredNotificationReferenceIsAcknowledgedWithoutChangingTheConfiguration()
    {
        _options.ExternalImageUrls[ClaudeCodeAssetPolicy.NotificationImageKey] = "https://example.com/custom.gif";
        var artwork = new ClaudeCodeArtworkState(() => _now);
        var observation = Observe(null, "UserPromptSubmit", Start);
        artwork.Apply(observation, Render(observation), _options,
            Acknowledged(Start) with { LargeImageKey = "https://example.com/custom.gif" });
        _now = Start.AddSeconds(6);
        Assert.Null(artwork.Apply(observation, Render(observation), _options, null).LargeImageKeyOverride);
        Assert.Equal("https://example.com/custom.gif", _options.ExternalImageUrls[ClaudeCodeAssetPolicy.NotificationImageKey]);
    }

    [Fact]
    public void Transcript_RealUserMessageRetainsInputTimeAcrossAssistantAndToolResults()
    {
        var directory = Path.Combine(Path.GetTempPath(), "claude-artwork-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "session.jsonl");
            File.WriteAllLines(path,
            [
                JsonSerializer.Serialize(new { sessionId = "main", cwd = Project, timestamp = Start, type = "user",
                    message = new { content = "private input" } }),
                JsonSerializer.Serialize(new { sessionId = "main", cwd = Project, timestamp = Start.AddSeconds(1), type = "assistant",
                    message = new { content = new[] { new { type = "tool_use", id = "read", name = "Read" } } } }),
                JsonSerializer.Serialize(new { sessionId = "main", cwd = Project, timestamp = Start.AddSeconds(2), type = "user",
                    message = new { content = new[] { new { type = "tool_result", tool_use_id = "read", content = "private result" } } } })
            ]);
            var observation = ClaudeCodeTranscriptActivityReader.Read(path)!;
            Assert.Equal(Start, observation.LastUserPromptAtUtc);
            Assert.Equal("PostToolUse", observation.EventName);
            Assert.DoesNotContain("private", JsonSerializer.Serialize(observation));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void NotificationDuration_MatchesThePreservedSourceAnimation()
    {
        using var image = System.Drawing.Image.FromFile(Path.Combine(AppContext.BaseDirectory,
            "Assets", "RpcArt", "ClaudeCode", "clawd-notification.gif"));
        var frameDelays = image.GetPropertyItem(0x5100)!.Value!;
        var centiseconds = Enumerable.Range(0, frameDelays.Length / 4)
            .Sum(index => BitConverter.ToInt32(frameDelays, index * 4));
        Assert.Equal(ClaudeCodeAssetPolicy.NotificationLoopDuration, TimeSpan.FromMilliseconds(centiseconds * 10));
    }

    private void AssertNotification(RenderedPresence presence) =>
        Assert.Equal(ClaudeCodeAssetPolicy.NotificationImageKey, DiscordAssetKeyResolver.ResolveLargeImageKey(_options, presence));

    private static ClaudeCodeSessionObservation Observe(ClaudeCodeSessionObservation? previous, string name,
        DateTimeOffset atUtc, string? tool = null) => ClaudeCodeSessionObservation.Apply(previous,
        new("main", Project, name, atUtc, ToolName: tool, ToolUseId: tool,
            AgentId: name is "SubagentStart" or "SubagentStop" ? "child" : null));

    private static RenderedPresence Render(ClaudeCodeSessionObservation observation) => new PresenceTemplateRenderer().Render(
        new PresenceTemplateOptions { Details = "{ModelName}", State = "{ActivityLine}" },
        ClaudeCodePresenceProjection.CreateContext(observation,
            new("project", Project, null, null, 0, 0, 0, []), new(false, 0, null), new(Start.UtcDateTime, TimeSpan.Zero)));

    private static DiscordPresenceSnapshot Acknowledged(DateTimeOffset atUtc) =>
        new("details", "state", "https://cdn.qualit.ly/clawd-notification.gif", null, null, null, null, null, null, [])
        { ProviderId = ProviderIds.ClaudeCode, AcknowledgedAtUtc = atUtc.UtcDateTime };
}
