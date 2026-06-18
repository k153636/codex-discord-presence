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

        var delay = PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.RunningCommand);

        Assert.Equal(TimeSpan.FromSeconds(1), delay);
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

        Assert.Equal(TimeSpan.FromSeconds(1), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.ApplyingEdits));
        Assert.Equal(TimeSpan.FromSeconds(1), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.UpdatingFiles));
        Assert.Equal(TimeSpan.FromSeconds(1), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.AnalyzingProject));
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

        Assert.Equal(TimeSpan.FromSeconds(1), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.Ready));
        Assert.Equal(TimeSpan.FromSeconds(1), PresenceRefreshPolicy.GetNextDelay(options, CodexActivityKind.Offline));
    }
}
