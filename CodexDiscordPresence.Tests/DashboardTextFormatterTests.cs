using Xunit;
using DiscordRPC;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardTextFormatterTests
{
    [Theory]
    [InlineData(ActivityType.Playing, "Playing:")]
    [InlineData(ActivityType.Listening, "Listening to:")]
    [InlineData(ActivityType.Watching, "Watching:")]
    [InlineData(ActivityType.Competing, "Competing in:")]
    public void FormatActivityType_UsesThePublishedDiscordActivityType(ActivityType activityType, string expected)
    {
        var presence = new DiscordPresenceSnapshot(
            "details",
            "state",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [])
        {
            ActivityType = activityType
        };

        Assert.Equal(expected, DashboardTextFormatter.FormatActivityType(presence));
    }

    [Fact]
    public void FormatActivityType_WithoutPublishedPresenceReturnsEmpty()
    {
        Assert.Equal("", DashboardTextFormatter.FormatActivityType(null));
    }

    [Theory]
    [InlineData("api", "API")]
    [InlineData("subscription", "subsc")]
    [InlineData("subsc", "subsc")]
    [InlineData(null, "")]
    [InlineData("unknown", "")]
    public void FormatBillingType_NormalizesKnownValues(string? value, string expected)
    {
        Assert.Equal(expected, DashboardTextFormatter.FormatBillingType(value));
    }

    [Fact]
    public void FormatRateLimitUsage_OnlyFormatsTheLiveFiveHourWindow()
    {
        Assert.Equal(
            "5h 25% used",
            DashboardTextFormatter.FormatRateLimitUsage(new RateLimitSnapshot(25, 300, DateTime.UtcNow)));
        Assert.Equal("", DashboardTextFormatter.FormatRateLimitUsage(null));
        Assert.Equal(
            "",
            DashboardTextFormatter.FormatRateLimitUsage(new RateLimitSnapshot(25, 60, DateTime.UtcNow)));
    }

    [Fact]
    public void FormatRateLimitReset_UsesHoursAndMinutes()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);
        var rateLimit = new RateLimitSnapshot(25, 300, now.AddHours(3).AddMinutes(2));

        Assert.Equal("reset 3h 2m", DashboardTextFormatter.FormatRateLimitReset(rateLimit, now));
    }

    [Fact]
    public void FormatRateLimitReset_ExpiredLimitUsesZero()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);
        var rateLimit = new RateLimitSnapshot(100, 300, now.AddSeconds(-1));

        Assert.Equal("reset 0h 0m", DashboardTextFormatter.FormatRateLimitReset(rateLimit, now));
    }

    [Fact]
    public void FormatRateLimitReset_WithoutLiveFiveHourWindowReturnsEmpty()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("", DashboardTextFormatter.FormatRateLimitReset(null, now));
        Assert.Equal(
            "",
            DashboardTextFormatter.FormatRateLimitReset(new RateLimitSnapshot(25, 60, now), now));
    }

    [Fact]
    public void FormatElapsed_UsesMinutesAndSecondsBelowAnHour()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("22:18", DashboardTextFormatter.FormatElapsed(now.AddMinutes(-22).AddSeconds(-18), now));
    }

    [Fact]
    public void FormatElapsed_UsesHoursMinutesAndSecondsAfterAnHour()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("3:02:18", DashboardTextFormatter.FormatElapsed(now.AddHours(-3).AddMinutes(-2).AddSeconds(-18), now));
    }

    [Fact]
    public void FormatElapsed_ClampsFutureStartToZero()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("0:00", DashboardTextFormatter.FormatElapsed(now.AddSeconds(1), now));
    }

    [Fact]
    public void FormatActivity_PrefersTheStateLastPublishedToDiscord()
    {
        var snapshot = new PresenceDashboardSnapshot(
            AppProfileKind.Codex,
            null,
            null,
            new RenderedPresence(
                "details",
                "predicted state",
                null,
                "small",
                [],
                null,
                CodexActivityKind.AnalyzingProject,
                RunningCommandKind.Unknown,
                ""),
            null,
            true,
            DateTime.UtcNow)
        {
            PublishedPresence = new DiscordPresenceSnapshot(
                "details",
                "new state from Discord payload",
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                [])
        };

        Assert.Equal("new state from Discord payload", DashboardTextFormatter.FormatActivity(snapshot, enabled: true));
    }

    [Fact]
    public void FormatActivity_WithoutLiveStateReturnsEmpty()
    {
        Assert.Equal("", DashboardTextFormatter.FormatActivity(PresenceDashboardSnapshot.Empty, enabled: true));
    }

    [Fact]
    public void FormatActivity_UnacknowledgedRenderDoesNotAppearAsPublished()
    {
        var snapshot = PresenceDashboardSnapshot.Empty with
        {
            Presence = new RenderedPresence("details", "not acknowledged", null, "", [], null,
                CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
        };
        Assert.Empty(DashboardTextFormatter.FormatActivity(snapshot, true));
    }

    [Theory]
    [InlineData(ProviderIds.Codex, "Codex")]
    [InlineData(ProviderIds.ClaudeCode, "Claude Code")]
    [InlineData(ProviderIds.Antigravity, "Antigravity CLI")]
    [InlineData(null, "No active provider")]
    [InlineData("other-provider", "No active provider")]
    public void ProviderPresentation_UsesOnlyItsOwnIdentity(string? provider, string expected)
    {
        Assert.Equal(expected, DashboardTextFormatter.FormatProviderName(provider));
    }

    [Fact]
    public void Usage_ClaudeAndUnknownProviderCannotBorrowCodexBillingOrLimits()
    {
        var snapshot = PresenceDashboardSnapshot.Empty with
        {
            ProviderId = ProviderIds.Codex,
            TokenUsage = new(100, 2m, "subsc", new(75, 300, DateTime.UtcNow.AddHours(1)))
        };
        Assert.Equal(3, DashboardTextFormatter.CreateMetrics(snapshot, DateTime.UtcNow).Length);
        Assert.Empty(DashboardTextFormatter.CreateMetrics(snapshot with { ProviderId = ProviderIds.ClaudeCode }, DateTime.UtcNow));
        Assert.Empty(DashboardTextFormatter.CreateMetrics(snapshot with { ProviderId = "unknown" }, DateTime.UtcNow));
        Assert.Empty(DashboardTextFormatter.CreateMetrics(snapshot with { HasNoActiveProvider = true }, DateTime.UtcNow));
    }

    [Fact]
    public void Usage_AntigravityUsesLowestReportedQuotaAndItsReset()
    {
        var now = DateTime.UtcNow;
        var snapshot = PresenceDashboardSnapshot.Empty with
        {
            ProviderId = ProviderIds.Antigravity,
            TokenUsage = new(null, null, "subsc", new(99, 300, now), "Pro",
                [new("model-a", 0.7m, now.AddHours(5)), new("model-b", 0.25m, now.AddHours(1))])
        };
        var metrics = DashboardTextFormatter.CreateMetrics(snapshot, now);
        Assert.Equal(["Plan", "Quota left", "Reset in"], metrics.Select(item => item.Label));
        Assert.Equal("25%", metrics[1].Value);
        Assert.Equal("1h 0m", metrics[2].Value);
    }

    [Theory]
    [InlineData(true, false, "Connected")]
    [InlineData(false, true, "Connecting")]
    [InlineData(false, false, "Disconnected")]
    public void ConnectionLabel_DistinguishesHandshakeFromDisconnected(bool connected, bool connecting, string expected)
    {
        Assert.Equal(expected, DashboardTextFormatter.FormatConnection(PresenceDashboardSnapshot.Empty with
        { IsDiscordConnected = connected, IsDiscordConnecting = connecting }));
    }
}
