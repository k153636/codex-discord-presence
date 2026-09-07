using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardTextFormatterTests
{
    [Theory]
    [InlineData("api", "API")]
    [InlineData("subscription", "subsc")]
    [InlineData("subsc", "subsc")]
    [InlineData(null, "unavailable")]
    public void FormatBillingType_NormalizesKnownValues(string? value, string expected)
    {
        Assert.Equal(expected, DashboardTextFormatter.FormatBillingType(value));
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
    public void FormatElapsed_UsesHourAndMinuteClock()
    {
        var now = new DateTime(2026, 9, 7, 3, 0, 0, DateTimeKind.Utc);

        Assert.Equal("3:02", DashboardTextFormatter.FormatElapsed(now.AddHours(-3).AddMinutes(-2), now));
    }
}
