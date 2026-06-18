using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceUpdatePolicyTests
{
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
