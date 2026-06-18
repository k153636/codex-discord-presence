using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordReconnectBackoffTests
{
    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 3)]
    [InlineData(2, 5)]
    [InlineData(3, 10)]
    [InlineData(4, 30)]
    [InlineData(10, 30)]
    public void GetDelay_UsesShortExponentialBackoff(int failedAttempts, int expectedSeconds)
    {
        var delay = DiscordReconnectBackoff.GetDelay(failedAttempts);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), delay);
    }
}
