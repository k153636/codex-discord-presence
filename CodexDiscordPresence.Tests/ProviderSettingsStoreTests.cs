using System.Text.Json;
using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class ProviderSettingsStoreTests
{
    [Fact]
    public void Load_MissingFile_UsesConfiguredDefaults()
    {
        var store = new ProviderSettingsStore();
        var result = store.Load(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"),
            Defaults());

        Assert.True(result[ProviderIds.Codex]);
        Assert.False(result[ProviderIds.Antigravity]);
    }

    [Fact]
    public void Load_LegacyEnabled_MapsToCodexOnlyWhenProviderOverrideIsAbsent()
    {
        var path = CreateTempFile("{\"Enabled\":false}");
        try
        {
            var store = new ProviderSettingsStore();
            var result = store.Load(path, Defaults());

            Assert.False(result[ProviderIds.Codex]);
            Assert.False(result[ProviderIds.Antigravity]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_ExplicitCodexOverride_WinsOverLegacyEnabled()
    {
        var path = CreateTempFile("{\"Enabled\":false,\"Providers\":{\"codex\":{\"Enabled\":true}}}");
        try
        {
            var result = new ProviderSettingsStore().Load(path, Defaults());

            Assert.True(result[ProviderIds.Codex]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_PreservesUnknownProvidersAndExistingStateProperties()
    {
        var path = CreateTempFile("""
            {
              "Enabled": true,
              "OtherState": "keep",
              "Providers": {
                "future-provider": { "Enabled": true, "Mode": "keep" }
              }
            }
            """);
        try
        {
            new ProviderSettingsStore().Save(
                path,
                new Dictionary<string, bool>
                {
                    [ProviderIds.Codex] = false,
                    ["future-provider"] = false
                });

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var root = document.RootElement;
            Assert.Equal("keep", root.GetProperty("OtherState").GetString());
            Assert.False(root.GetProperty("Providers").GetProperty(ProviderIds.Codex).GetProperty("Enabled").GetBoolean());
            Assert.False(root.GetProperty("Providers").GetProperty("future-provider").GetProperty("Enabled").GetBoolean());
            Assert.Equal("keep", root.GetProperty("Providers").GetProperty("future-provider").GetProperty("Mode").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MalformedJson_ReturnsDefaultsWithoutThrowing()
    {
        var path = CreateTempFile("{ not valid json");
        try
        {
            var result = new ProviderSettingsStore().Load(path, Defaults());

            Assert.True(result[ProviderIds.Codex]);
            Assert.False(result[ProviderIds.Antigravity]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_UnknownProvider_PreservesItsEnabledState()
    {
        var path = CreateTempFile("{\"Providers\":{\"future-provider\":{\"Enabled\":true}}}");
        try
        {
            var result = new ProviderSettingsStore().Load(path, Defaults());

            Assert.True(result["future-provider"]);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static Dictionary<string, ProviderOptions> Defaults() => new(StringComparer.OrdinalIgnoreCase)
    {
        [ProviderIds.Codex] = new() { Enabled = true },
        [ProviderIds.Antigravity] = new() { Enabled = false }
    };

    private static string CreateTempFile(string contents)
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexProviderSettings_" + Guid.NewGuid() + ".json");
        File.WriteAllText(path, contents);
        return path;
    }
}
