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
}
