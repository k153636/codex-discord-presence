using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ActivityRepeatCountTrackerTests
{
    [Fact]
    public void GetAnalyzingRepeatCount_DoesNotIncreaseWithoutNewTaskStart()
    {
        var taskStartedAt = DateTime.UtcNow;

        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            taskStartedAt,
            taskStartedAt,
            4);

        Assert.Equal(4, repeatCount);
    }

    [Fact]
    public void GetAnalyzingRepeatCount_IncreasesWhenTaskStartChanges()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(-3),
            1);

        Assert.Equal(2, repeatCount);
    }

    [Fact]
    public void GetAnalyzingRepeatCount_AllowsUnlimitedGrowth()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            DateTime.UtcNow,
            DateTime.UtcNow.AddSeconds(-3),
            18);

        Assert.Equal(19, repeatCount);
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

    [Fact]
    public void GetAnalyzingRepeatCount_DoesNotIncreaseWithoutTaskStartEvidence()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            null,
            DateTime.UtcNow.AddSeconds(-3),
            4);

        Assert.Equal(4, repeatCount);
    }

    [Fact]
    public void GetAnalyzingRepeatCount_IncreasesWhenTaskStartAppearsAfterUnknownState()
    {
        var repeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            CodexActivityKind.AnalyzingProject,
            CodexActivityKind.AnalyzingProject,
            DateTime.UtcNow,
            null,
            4);

        Assert.Equal(5, repeatCount);
    }
}
