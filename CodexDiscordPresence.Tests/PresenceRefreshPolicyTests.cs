using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceRefreshPolicyTests
{
    [Fact]
    public void GetNextDelay_UsesFastestIntervalForRunningCommand()
    {
        var options = new PresenceTemplateOptions
        {
            RunningCommandUpdateIntervalMilliseconds = 500,
            ActiveUpdateIntervalSeconds = 2,
            IdleUpdateIntervalSeconds = 8
        };

        var delay = PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.RunningCommand, 2);

        Assert.Equal(TimeSpan.FromMilliseconds(500), delay);
    }

    [Fact]
    public void GetNextDelay_UsesActiveIntervalForWorkStates()
    {
        var options = new PresenceTemplateOptions
        {
            RunningCommandUpdateIntervalMilliseconds = 500,
            ActiveUpdateIntervalSeconds = 2,
            IdleUpdateIntervalSeconds = 8
        };

        Assert.Equal(TimeSpan.FromSeconds(2), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.ApplyingEdits, 2));
        Assert.Equal(TimeSpan.FromSeconds(2), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.CoordinatingChanges, 2));
        Assert.Equal(TimeSpan.FromSeconds(3), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.AnalyzingProject, 2));
    }

    [Fact]
    public void GetNextDelay_UsesIdleIntervalForReadyAndOffline()
    {
        var options = new PresenceTemplateOptions
        {
            RunningCommandUpdateIntervalMilliseconds = 500,
            ActiveUpdateIntervalSeconds = 2,
            IdleUpdateIntervalSeconds = 8
        };

        Assert.Equal(TimeSpan.FromSeconds(8), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.Ready, 2));
        Assert.Equal(TimeSpan.FromSeconds(8), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.Offline, 2));
    }
}
