using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceUpdatePolicyTests
{
    [Theory]
    [InlineData(15, false)]
    [InlineData(59, false)]
    [InlineData(60, true)]
    public void ShouldSendKeepAlive_UnchangedPayload_UsesOneMinuteInterval(int seconds, bool expected)
    {
        var now = new DateTime(2026, 10, 9, 0, 1, 0, DateTimeKind.Utc);
        Assert.Equal(expected, PresenceUpdatePolicy.ShouldSendKeepAlive(
            now.AddSeconds(-seconds), now, PresenceUpdatePolicy.KeepAliveInterval));
    }

    [Fact]
    public void ShouldSendKeepAlive_WhenNoSuccessfulUpdateYet_ReturnsTrue()
    {
        var now = DateTime.UtcNow;

        Assert.True(PresenceUpdatePolicy.ShouldSendKeepAlive(default, now, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void ShouldSendKeepAlive_WhenWithinInterval_ReturnsFalse()
    {
        var now = DateTime.UtcNow;
        var lastSuccessfulUpdateUtc = now.AddSeconds(-14);

        Assert.False(PresenceUpdatePolicy.ShouldSendKeepAlive(lastSuccessfulUpdateUtc, now, TimeSpan.FromSeconds(15)));
    }

    [Fact]
    public void ShouldSendKeepAlive_WhenPastInterval_ReturnsTrue()
    {
        var now = DateTime.UtcNow;
        var lastSuccessfulUpdateUtc = now.AddSeconds(-16);

        Assert.True(PresenceUpdatePolicy.ShouldSendKeepAlive(lastSuccessfulUpdateUtc, now, TimeSpan.FromSeconds(15)));
    }
}
