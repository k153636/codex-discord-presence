using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class RuntimeTimingSettingsTests
{
    [Fact]
    public void ApplyTo_UpdatesOnlyTimingFields()
    {
        var source = new AppOptions
        {
            UpdateIntervalSeconds = 7,
            Presence =
            {
                ActiveUpdateIntervalSeconds = 3,
                RunningCommandUpdateIntervalSeconds = 11,
                RunningCommandUpdateIntervalMilliseconds = 250,
                IdleUpdateIntervalSeconds = 19,
                Details = "Keep this",
                State = "And this",
                LargeImageText = "Also keep this",
                SmallImageText = "And this too"
            }
        };

        var target = new AppOptions
        {
            UpdateIntervalSeconds = 2,
            Presence =
            {
                ActiveUpdateIntervalSeconds = 1,
                RunningCommandUpdateIntervalSeconds = 1,
                RunningCommandUpdateIntervalMilliseconds = 500,
                IdleUpdateIntervalSeconds = 8,
                Details = "Original details",
                State = "Original state",
                LargeImageText = "Original large text",
                SmallImageText = "Original small text"
            }
        };

        RuntimeTimingSettings.From(source).ApplyTo(target);

        Assert.Equal(7, target.UpdateIntervalSeconds);
        Assert.Equal(3, target.Presence.ActiveUpdateIntervalSeconds);
        Assert.Equal(11, target.Presence.RunningCommandUpdateIntervalSeconds);
        Assert.Equal(250, target.Presence.RunningCommandUpdateIntervalMilliseconds);
        Assert.Equal(19, target.Presence.IdleUpdateIntervalSeconds);
        Assert.Equal("Original details", target.Presence.Details);
        Assert.Equal("Original state", target.Presence.State);
        Assert.Equal("Original large text", target.Presence.LargeImageText);
        Assert.Equal("Original small text", target.Presence.SmallImageText);
    }
}
