using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeUsageTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Parse_OfficialStatusLine_UsesFiveHourUsageAndCostWithoutConfusingContextWithTotalTokens()
    {
        var usage = ClaudeCodeUsageObservation.Parse(Payload("main", ProjectPath, 23.5m), Now)!;
        Assert.Equal(24, usage.RateLimit!.UsedPercent);
        Assert.Equal(300, usage.RateLimit.WindowDurationMinutes);
        Assert.Equal(Now.AddHours(3).UtcDateTime, usage.RateLimit.ResetAtUtc);
        Assert.Equal(0.18m, usage.EstimatedCostUsd);
        Assert.True(usage.HasSubscriptionUsage);
        Assert.DoesNotContain("context_window", JsonSerializer.Serialize(usage));
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(usage));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"session_id\":\"main\",\"cwd\":\"relative\"}")]
    [InlineData("{\"session_id\":\"main\",\"cwd\":{}}")]
    [InlineData("{incomplete")]
    public void Parse_InvalidIdentityOrPayload_FailsClosed(string json) => Assert.Null(ClaudeCodeUsageObservation.Parse(json, Now));

    [Fact]
    public void Parse_WeeklyOnlyEstablishesSubscription_WithoutReplacingTheCodexFiveHourWindow()
    {
        var json = JsonNode.Parse(Payload("main", ProjectPath))!;
        json["rate_limits"]!["seven_day"] = json["rate_limits"]!["five_hour"]!.DeepClone();
        json["rate_limits"]!.AsObject().Remove("five_hour");
        var usage = ClaudeCodeUsageObservation.Parse(json.ToJsonString(), Now)!;
        Assert.True(usage.HasSubscriptionUsage);
        Assert.Null(usage.RateLimit);
    }

    [Fact]
    public void Parse_CostAndContextAlone_DoNotInventSubscriptionOrApiBilling()
    {
        var json = JsonNode.Parse(Payload("main", ProjectPath))!;
        json.AsObject().Remove("rate_limits");
        var usage = ClaudeCodeUsageObservation.Parse(json.ToJsonString(), Now)!;
        Assert.False(usage.HasSubscriptionUsage);
        Assert.NotNull(usage.EstimatedCostUsd);
        Assert.Null(usage.RateLimit);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void Parse_InvalidPercentage_OmitsUsageRatherThanClampingToAReportedValue(int percentage)
    {
        var usage = ClaudeCodeUsageObservation.Parse(Payload("main", ProjectPath, percentage), Now)!;
        Assert.Null(usage.RateLimit);
        Assert.False(usage.HasSubscriptionUsage);
    }

    [Fact]
    public void Store_MatchesSessionAndProject_IgnoresEndedFutureAndOutOfOrderObservations()
    {
        InDirectory(directory =>
        {
            var store = new ClaudeCodeUsageStore(directory);
            var usage = ClaudeCodeUsageObservation.Parse(Payload("main", ProjectPath), Now)!;
            store.Write(usage);
            store.Write(usage with { ObservedAtUtc = Now.AddSeconds(-1), EstimatedCostUsd = 99 });
            var session = Session();
            Assert.Equal(0.18m, store.GetForSession(session, Now)!.EstimatedCostUsd);
            Assert.Null(store.GetForSession(session with { SessionId = "other" }, Now));
            Assert.Null(store.GetForSession(session with { ProjectPath = ProjectPath + "-other" }, Now));
            Assert.Null(store.GetForSession(session with { Ended = true }, Now));
            Assert.Null(store.GetForSession(session, Now.AddSeconds(-1)));
            Assert.DoesNotContain("secret", File.ReadAllText(Directory.GetFiles(directory, "*.json")[0]));
        });
    }

    [Fact]
    public void Tokens_AccumulatesCompleteMainSessionMessagesIncludingCache_DeduplicatesStreamingBlocks()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "transcript.jsonl");
            File.WriteAllText(path, Message("a", 60, 5) + Message("a", 60, 10) + Message("b", 30, 5) +
                Message("child", 999, 999, sidechain: true) + Message("other", 999, 999, sessionId: "other"));
            var provider = new ClaudeCodeTokenUsageProvider(new ClaudeCodeUsageStore(Path.Combine(directory, "usage")));
            var session = Session(path);
            Assert.Equal(145, provider.GetSnapshot(session, new(), Now).TotalTokens);
            Assert.Equal(145, provider.GetSnapshot(session, new(), Now).TotalTokens);
            File.AppendAllText(path, Message("b", 30, 15) + Message("c", 10, 5));
            Assert.Equal(190, provider.GetSnapshot(session, new(), Now).TotalTokens);
        });
    }

    [Fact]
    public void Tokens_PartialLastLine_IsCountedOnceAfterCompletion_AndTruncationResetsTotals()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "transcript.jsonl");
            var message = Message("b", 30, 15);
            File.WriteAllText(path, Message("a", 60, 10) + message[..(message.Length / 2)]);
            var provider = new ClaudeCodeTokenUsageProvider(new ClaudeCodeUsageStore(Path.Combine(directory, "usage")));
            var session = Session(path);
            Assert.Equal(90, provider.GetSnapshot(session, new(), Now).TotalTokens);
            File.AppendAllText(path, message[(message.Length / 2)..]);
            Assert.Equal(155, provider.GetSnapshot(session, new(), Now).TotalTokens);
            File.WriteAllText(path, Message("c", 10, 5));
            Assert.Equal(35, provider.GetSnapshot(session, new(), Now).TotalTokens);
        });
    }

    [Fact]
    public void Tokens_SameLengthRewriteAndSessionSwitch_DoNotRetainPriorSessionTotals()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "transcript.jsonl");
            File.WriteAllText(path, Message("a", 60, 10));
            var provider = new ClaudeCodeTokenUsageProvider(new ClaudeCodeUsageStore(Path.Combine(directory, "usage")));
            Assert.Equal(90, provider.GetSnapshot(Session(path), new(), Now).TotalTokens);
            File.WriteAllText(path, Message("a", 20, 10));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
            Assert.Equal(50, provider.GetSnapshot(Session(path), new(), Now).TotalTokens);
            Assert.Null(provider.GetSnapshot(Session(path) with { SessionId = "other" }, new(), Now).TotalTokens);
        });
    }

    [Fact]
    public void Snapshot_ExpiredLimitAndMissingTokens_OmitUnavailableValuesAndKeepKnownBilling()
    {
        InDirectory(directory =>
        {
            var store = new ClaudeCodeUsageStore(directory);
            store.Write(ClaudeCodeUsageObservation.Parse(Payload("main", ProjectPath), Now)!);
            var provider = new ClaudeCodeTokenUsageProvider(store);
            var snapshot = provider.GetSnapshot(Session(), new(), Now.AddHours(4));
            Assert.Null(snapshot.TotalTokens);
            Assert.Null(snapshot.RateLimit);
            Assert.Equal("subsc", snapshot.BillingType);
            Assert.Equal(0.18m, snapshot.EstimatedCostUsd);
            var disabled = provider.GetSnapshot(Session(), new() { Enabled = false }, Now);
            Assert.Null(disabled.TotalTokens);
            Assert.Null(disabled.EstimatedCostUsd);
            Assert.NotNull(disabled.RateLimit);
        });
    }

    [Fact]
    public void Renderer_ClaudeAndCodexUseIdenticalFiveSecondCycleAndUsageFormatting_AndActiveWorkStopsRotation()
    {
        var current = Now.UtcDateTime;
        var renderer = new PresenceTemplateRenderer(() => current);
        var template = new PresenceTemplateOptions { Details = "{ModelName} • {Tokens}", WaitingDetails = "{Cost} {BillingType}{RateLimitDetails}", State = "{ActivityLine}" };
        var context = Context(new(12_400, 0.18m, "subsc", new(25, 300, Now.AddHours(3).UtcDateTime)));
        Assert.Equal("Claude Code • 12.4K Token", renderer.Render(template, context).Details);
        current = Now.AddSeconds(5).UtcDateTime;
        var claudeUsage = renderer.Render(template, context).Details;
        Assert.Equal(renderer.Render(template, context with { ProviderId = ProviderIds.Codex }).Details, claudeUsage);
        Assert.Contains("subsc", claudeUsage);
        Assert.Contains("25%", claudeUsage);
        Assert.Contains("3h 0m", claudeUsage);
        current = Now.AddSeconds(10).UtcDateTime;
        Assert.Equal("Claude Code • 12.4K Token", renderer.Render(template, context).Details);
        Assert.Equal("Claude Code • 12.4K Token", renderer.Render(template, Context(context.TokenUsage, "UserPromptSubmit")).Details);
    }

    [Fact]
    public void Dashboard_ClaudeAndCodexShareMetricsAndMissingUsageMessage()
    {
        var snapshot = PresenceDashboardSnapshot.Empty with
        {
            HasNoActiveProvider = false,
            ProviderId = ProviderIds.ClaudeCode,
            TokenUsage = new(12_400, 0.18m, "subsc", new(25, 300, Now.AddHours(3).UtcDateTime))
        };
        Assert.Equal(DashboardTextFormatter.CreateMetrics(snapshot with { ProviderId = ProviderIds.Codex }, Now.UtcDateTime),
            DashboardTextFormatter.CreateMetrics(snapshot, Now.UtcDateTime));
        Assert.Equal(DashboardTextFormatter.FormatUsageNote(snapshot with { ProviderId = ProviderIds.Codex }), DashboardTextFormatter.FormatUsageNote(snapshot));
    }

    private static PresenceContext Context(TokenUsageSnapshot usage, string eventName = "Stop") => ClaudeCodePresenceProjection.CreateContext(
        Session() with { EventName = eventName }, new("project", ProjectPath, null, null, 0, 0, 0, []), new(false, 0, null),
        new(Now.UtcDateTime, TimeSpan.Zero), tokenUsage: usage);

    private static string ProjectPath => Path.Combine(Path.GetTempPath(), "ClaudeUsageProject");
    private static ClaudeCodeSessionObservation Session(string? path = null) => new("main", ProjectPath, Now, Now, "Stop", false, null, path, [], []);

    private static string Payload(string session, string projectPath, decimal percentage = 25) => JsonSerializer.Serialize(new
    {
        session_id = session, cwd = projectPath, workspace = new { current_dir = projectPath },
        context_window = new { total_input_tokens = 99999, total_output_tokens = 999 },
        cost = new { total_cost_usd = 0.18m },
        rate_limits = new { five_hour = new { used_percentage = percentage, resets_at = Now.AddHours(3).ToUnixTimeSeconds() } },
        prompt = "secret should never persist"
    });

    private static string Message(string id, int input, int output, bool sidechain = false, string sessionId = "main") => JsonSerializer.Serialize(new
    {
        type = "assistant", sessionId, isSidechain = sidechain, uuid = Guid.NewGuid().ToString(),
        message = new { id, role = "assistant", content = new[] { new { type = "text", text = "private body" } },
            usage = new { input_tokens = input, output_tokens = output, cache_creation_input_tokens = 5, cache_read_input_tokens = 15 } }
    }) + "\n";

    private static void InDirectory(Action<string> action)
    {
        var path = Path.Combine(Path.GetTempPath(), "ClaudeUsageTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        try { action(path); }
        finally { Directory.Delete(path, true); }
    }
}
