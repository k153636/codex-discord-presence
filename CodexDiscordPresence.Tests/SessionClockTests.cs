using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class SessionClockTests
{
    [Fact]
    public void GetSnapshot_UsesOneRuntimeStartAcrossProviderObservations()
    {
        var startedAt = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);
        var clock = new SessionClock(startedAt);

        var codexSnapshot = clock.GetSnapshot(startedAt.AddMinutes(2));
        var antigravitySnapshot = clock.GetSnapshot(startedAt.AddMinutes(7));

        Assert.Equal(startedAt, codexSnapshot.StartedAt);
        Assert.Equal(startedAt, antigravitySnapshot.StartedAt);
        Assert.Equal(TimeSpan.FromMinutes(2), codexSnapshot.Elapsed);
        Assert.Equal(TimeSpan.FromMinutes(7), antigravitySnapshot.Elapsed);
    }

    [Fact]
    public void NewClock_RepresentsAnewRpcRuntimeSession()
    {
        var firstStart = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);
        var secondStart = firstStart.AddHours(1);

        var firstSnapshot = new SessionClock(firstStart).GetSnapshot(firstStart.AddMinutes(5));
        var secondSnapshot = new SessionClock(secondStart).GetSnapshot(secondStart.AddMinutes(1));

        Assert.Equal(firstStart, firstSnapshot.StartedAt);
        Assert.Equal(secondStart, secondSnapshot.StartedAt);
        Assert.NotEqual(firstSnapshot.StartedAt, secondSnapshot.StartedAt);
    }
}
