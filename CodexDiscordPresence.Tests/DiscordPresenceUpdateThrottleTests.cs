namespace CodexDiscordPresence.Tests;

public sealed class DiscordPresenceUpdateThrottleTests
{
    [Fact]
    public void TryReserve_AllowsFiveUpdatesWithinTheDiscordWindow()
    {
        var throttle = new DiscordPresenceUpdateThrottle();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < DiscordPresenceUpdateThrottle.MaxUpdatesPerWindow; index++)
        {
            Assert.True(throttle.TryReserve(start.AddSeconds(index), out _));
        }
    }

    [Fact]
    public void TryReserve_RejectsTheSixthUpdateUntilTheOldestReservationExpires()
    {
        var throttle = new DiscordPresenceUpdateThrottle();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < DiscordPresenceUpdateThrottle.MaxUpdatesPerWindow; index++)
        {
            Assert.True(throttle.TryReserve(start.AddSeconds(index), out _));
        }

        Assert.False(throttle.TryReserve(start.AddSeconds(19), out var retryAt));
        Assert.Equal(start.AddSeconds(20), retryAt);
        Assert.True(throttle.TryReserve(start.AddSeconds(20), out _));
    }

    [Fact]
    public void Reset_AllowsAReconnectedClientToStartWithAnEmptyWindow()
    {
        var throttle = new DiscordPresenceUpdateThrottle();
        var start = new DateTime(2026, 9, 8, 0, 0, 0, DateTimeKind.Utc);

        for (var index = 0; index < DiscordPresenceUpdateThrottle.MaxUpdatesPerWindow; index++)
        {
            Assert.True(throttle.TryReserve(start.AddSeconds(index), out _));
        }

        throttle.Reset();

        Assert.True(throttle.TryReserve(start.AddSeconds(5), out _));
    }
}
