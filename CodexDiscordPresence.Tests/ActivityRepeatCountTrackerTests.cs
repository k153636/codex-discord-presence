using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ActivityRepeatCountTrackerTests
{
    [Fact]
    public void GetAnalyzingRepeatCount_DoesNotIncreaseWithoutNewObservation()
    {
        var observedAt = DateTime.UtcNow;

        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            observedAt,
            observedAt,
            4);

        Assert.Equal(4, repeatCount);
    }

    [Fact]
    public void GetAnalyzingRepeatCount_IncreasesWhenObservationChanges()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(-3),
            4);

        Assert.Equal(5, repeatCount);
    }

    [Fact]
    public void GetAnalyzingRepeatCount_ResetsWhenActivityChanges()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.RunningCommand,
            CodexActivityKind.AnalyzingProject,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(-3),
            4);

        Assert.Equal(1, repeatCount);
    }
}
