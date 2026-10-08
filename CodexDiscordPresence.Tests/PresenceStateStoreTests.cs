using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceStateStoreTests
{
    [Fact]
    public void SaveAndLoad_PersistsEnabledState()
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
    public void Save_PersistsEnabledStateOnly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            var store = new PresenceStateStore();
            store.Save(statePath, new PresenceRuntimeState { Enabled = true });

            var json = File.ReadAllText(statePath);

            Assert.Contains("\"Enabled\": true", json);
            Assert.DoesNotContain("SessionStartedAtUtc", json);
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
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void SaveAndLoad_PersistsProviderStates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            var state = new PresenceRuntimeState { Enabled = false };
            state.InitializeProviderEnabled(new Dictionary<string, bool>
            {
                [ProviderIds.Codex] = false,
                [ProviderIds.Antigravity] = true,
                ["future-provider"] = false
            });

            var store = new PresenceStateStore();
            store.Save(statePath, state);

            var loaded = store.Load(statePath);

            Assert.False(loaded.Enabled);
            Assert.False(loaded.IsProviderEnabled(ProviderIds.Codex));
            Assert.True(loaded.IsProviderEnabled(ProviderIds.Antigravity));
            Assert.False(loaded.IsProviderEnabled("future-provider"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_CombinesConfiguredDefaultsWithPersistedProviderStates()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            File.WriteAllText(
                statePath,
                "{\"Enabled\":true,\"Providers\":{\"codex\":{\"Enabled\":false},\"future-provider\":{\"Enabled\":true}}}");

            var configuredProviders = new Dictionary<string, ProviderOptions>
            {
                [ProviderIds.Codex] = new() { Enabled = true },
                [ProviderIds.Antigravity] = new() { Enabled = false },
                ["new-provider"] = new() { Enabled = true }
            };

            var loaded = new PresenceStateStore().Load(statePath, configuredProviders);

            Assert.False(loaded.IsProviderEnabled(ProviderIds.Codex));
            Assert.False(loaded.IsProviderEnabled(ProviderIds.Antigravity));
            Assert.True(loaded.IsProviderEnabled("new-provider"));
            Assert.True(loaded.IsProviderEnabled("future-provider"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_LegacyEnabledInitializesCodexWithoutOverridingOtherDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            File.WriteAllText(statePath, "{\"Enabled\":false}");

            var loaded = new PresenceStateStore().Load(
                statePath,
                new Dictionary<string, ProviderOptions>
                {
                    [ProviderIds.Codex] = new() { Enabled = true },
                    [ProviderIds.Antigravity] = new() { Enabled = false }
                });

            Assert.False(loaded.Enabled);
            Assert.False(loaded.IsProviderEnabled(ProviderIds.Codex));
            Assert.False(loaded.IsProviderEnabled(ProviderIds.Antigravity));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Save_PreservesUnknownPropertiesAndProviders()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            File.WriteAllText(
                statePath,
                "{\"Enabled\":true,\"UnknownSetting\":\"keep\",\"Providers\":{\"future-provider\":{\"Enabled\":true,\"FutureOption\":42}}}");

            var state = new PresenceRuntimeState();
            state.InitializeProviderEnabled(new Dictionary<string, bool>
            {
                [ProviderIds.Codex] = true,
                [ProviderIds.Antigravity] = false
            });

            new PresenceStateStore().Save(statePath, state);

            var json = File.ReadAllText(statePath);
            Assert.Contains("\"UnknownSetting\": \"keep\"", json);
            Assert.Contains("\"future-provider\"", json);
            Assert.Contains("\"FutureOption\": 42", json);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void Load_BrokenJsonUsesConfiguredDefaults()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexPresenceStateTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var statePath = Path.Combine(tempDir, "presence-state.json");

        try
        {
            File.WriteAllText(statePath, "not-json");

            var loaded = new PresenceStateStore().Load(
                statePath,
                new Dictionary<string, ProviderOptions>
                {
                    [ProviderIds.Codex] = new() { Enabled = true },
                    [ProviderIds.Antigravity] = new() { Enabled = false }
                });

            Assert.True(loaded.Enabled);
            Assert.True(loaded.IsProviderEnabled(ProviderIds.Codex));
            Assert.False(loaded.IsProviderEnabled(ProviderIds.Antigravity));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
