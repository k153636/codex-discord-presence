using System.Text;

namespace CodexDiscordPresence.Tests;

public sealed class AntigravityActivityEnricherTests
{
    [Fact]
    public void TranscriptReader_ReadsLatestStructuredToolCall()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.TranscriptPath,
            """
            {"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-09-12T10:42:19Z","tool_calls":[{"name":"search_web","args":{"toolAction":"Searching the web","toolSummary":"Model search","query":"pricing"}}]}
            """,
            Encoding.UTF8);

        var operation = new AntigravityTranscriptActivityReader().ReadLatest(fixture.TranscriptPath);

        Assert.NotNull(operation);
        Assert.Equal(CodexOperationKind.Research, operation.Kind);
        Assert.Equal("search_web", operation.ToolName);
        Assert.Equal("Searching the web", operation.Action);
        Assert.True(operation.IsCompleted);
        Assert.Equal(
            DateTimeOffset.Parse("2026-09-12T10:42:19Z"),
            operation.ObservedAtUtc);
    }

    [Fact]
    public void ConfirmationReader_ReportsPendingRequestUntilItIsResolved()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.LogPath,
            "I0912 10:42:17.629324     456 tool_confirmation_manager.go:197] Surfacing tool confirmation: \"ReadUrlContent\" at step 25\n",
            Encoding.UTF8);
        var reader = new AntigravityCliConfirmationReader(fixture.LogDirectory, TimeZoneInfo.Utc);
        var observationAt = DateTimeOffset.Parse("2026-09-12T10:42:20Z");

        var pending = reader.ReadPending("conversation-id", observationAt, observationAt);

        Assert.True(pending.IsPending);
        Assert.Equal("ReadUrlContent", pending.ToolName);
        Assert.Equal(25, pending.StepIndex);

        File.AppendAllText(
            fixture.LogPath,
            "I0912 10:42:21.000000     210 input_loop.go:648] Responding to tool confirmation: convID=conversation-id, stepIdx=25, approved=true, sandboxOverride=false\n",
            Encoding.UTF8);

        var resolved = reader.ReadPending(
            "conversation-id",
            observationAt.AddSeconds(3),
            observationAt.AddSeconds(3));

        Assert.False(resolved.IsPending);
    }

    [Fact]
    public void Enricher_ResolvesConversationTranscriptAndConfirmationEvidence()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.TranscriptPath,
            """
            {"step_index":1,"source":"MODEL","type":"PLANNER_RESPONSE","status":"DONE","created_at":"2026-09-12T10:42:19Z","tool_calls":[{"name":"read_url_content","args":{"toolAction":"Reading pricing page","Url":"https://example.invalid/pricing"}}]}
            """,
            Encoding.UTF8);
        File.WriteAllText(
            fixture.LogPath,
            "I0912 10:42:17.629324     456 tool_confirmation_manager.go:197] Surfacing tool confirmation: \"ReadUrlContent\" at step 25\n",
            Encoding.UTF8);

        var observedAt = DateTimeOffset.Parse("2026-09-12T10:42:20Z");
        var observation = new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            observedAt,
            ProviderAgentState.ToolUse,
            null,
            null,
            "conversation-id");
        var enricher = new AntigravityActivityEnricher(
            fixture.UserProfile,
            new AntigravityTranscriptActivityReader(),
            new AntigravityCliConfirmationReader(fixture.LogDirectory, TimeZoneInfo.Utc));

        var enriched = enricher.Enrich(observation, observedAt);

        Assert.True(enriched.IsWaitingForInput);
        Assert.Equal(CodexOperationKind.Research, enriched.Operation?.Kind);
        Assert.Equal("read_url_content", enriched.Operation?.ToolName);
    }

    private sealed class TemporaryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "Antigravity Activity Tests",
            Guid.NewGuid().ToString("N"));

        internal TemporaryFixture()
        {
            UserProfile = Path.Combine(_root, "user profile");
            LogDirectory = Path.Combine(_root, "logs");
            Directory.CreateDirectory(LogDirectory);
            TranscriptPath = Path.Combine(
                UserProfile,
                ".gemini",
                "antigravity-cli",
                "brain",
                "conversation-id",
                ".system_generated",
                "logs",
                "transcript_full.jsonl");
            Directory.CreateDirectory(Path.GetDirectoryName(TranscriptPath)!);
            LogPath = Path.Combine(LogDirectory, "cli-test.log");
        }

        internal string UserProfile { get; }
        internal string LogDirectory { get; }
        internal string TranscriptPath { get; }
        internal string LogPath { get; }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
