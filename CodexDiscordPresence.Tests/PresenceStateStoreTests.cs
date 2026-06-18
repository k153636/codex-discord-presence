using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceStateStoreTests
{
    [Fact]
    public void SaveAndLoad_RoundTripsEnabledState()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            var store = new PresenceStateStore();
            store.Save(statePath, new PresenceRuntimeState { Enabled = false });

            var loaded = store.Load(statePath);

            Assert.False(loaded.Enabled);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_WhenFileMissing_DefaultsToEnabled()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "missing.json");

        try
        {
            var store = new PresenceStateStore();

            var loaded = store.Load(statePath);

            Assert.True(loaded.Enabled);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
