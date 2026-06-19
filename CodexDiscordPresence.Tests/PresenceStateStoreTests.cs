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
            Assert.Null(loaded.SessionStartedAtUtc);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndLoad_RoundTripsSessionStartedAt()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            var startedAt = new DateTime(2026, 6, 20, 12, 0, 0, DateTimeKind.Utc);
            var store = new PresenceStateStore();
            store.Save(statePath, new PresenceRuntimeState { Enabled = true, SessionStartedAtUtc = startedAt });

            var loaded = store.Load(statePath);

            Assert.True(loaded.Enabled);
            Assert.Equal(startedAt, loaded.SessionStartedAtUtc);
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
            Assert.Null(loaded.SessionStartedAtUtc);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_OldFormatFile_PreservesCompatibility()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            File.WriteAllText(statePath, "{\"Enabled\":false}");

            var store = new PresenceStateStore();

            var loaded = store.Load(statePath);

            Assert.False(loaded.Enabled);
            Assert.Null(loaded.SessionStartedAtUtc);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
