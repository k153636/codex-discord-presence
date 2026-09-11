using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

public sealed class ProviderSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "CodexDiscordPresence", "presence-state.json");
    }

    public IReadOnlyDictionary<string, bool> Load(
        string path,
        IReadOnlyDictionary<string, ProviderOptions> configuredProviders)
    {
        var enabledByProvider = configuredProviders.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.Enabled,
            StringComparer.OrdinalIgnoreCase);

        try
        {
            if (!File.Exists(path))
            {
                return enabledByProvider;
            }

            var root = JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) as JsonObject;
            if (root is null)
            {
                return enabledByProvider;
            }

            var providers = FindProperty(root, "Providers") as JsonObject;
            if (providers is not null)
            {
                foreach (var (providerId, value) in providers)
                {
                    if (value is JsonObject provider &&
                        TryGetBoolean(provider, "Enabled", out var enabled))
                    {
                        enabledByProvider[providerId] = enabled;
                    }
                }
            }

            if (!ContainsProvider(providers, ProviderIds.Codex) &&
                TryGetBoolean(root, "Enabled", out var legacyEnabled))
            {
                enabledByProvider[ProviderIds.Codex] = legacyEnabled;
            }

            return enabledByProvider;
        }
        catch
        {
            return enabledByProvider;
        }
    }

    public void Save(string path, IReadOnlyDictionary<string, bool> enabledByProvider)
    {
        try
        {
            var root = LoadRoot(path);
            var providers = FindProperty(root, "Providers") as JsonObject;
            if (providers is null)
            {
                providers = new JsonObject();
                root["Providers"] = providers;
            }

            foreach (var (providerId, enabled) in enabledByProvider)
            {
                var provider = FindProperty(providers, providerId) as JsonObject;
                if (provider is null)
                {
                    provider = new JsonObject();
                    providers[providerId] = provider;
                }

                var enabledProperty = FindPropertyName(provider, "Enabled") ?? "Enabled";
                provider[enabledProperty] = enabled;
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, root.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save provider settings: {ex.Message}");
        }
    }

    private static JsonObject LoadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        try
        {
            return JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) as JsonObject ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }

    private static bool ContainsProvider(JsonObject? providers, string providerId) =>
        providers is not null && FindProperty(providers, providerId) is not null;

    private static bool TryGetBoolean(JsonObject node, string propertyName, out bool value)
    {
        var property = FindProperty(node, propertyName);
        if (property is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out value))
        {
            return true;
        }

        value = false;
        return false;
    }

    private static JsonNode? FindProperty(JsonObject node, string propertyName)
    {
        var actualName = FindPropertyName(node, propertyName);
        return actualName is null ? null : node[actualName];
    }

    private static string? FindPropertyName(JsonObject node, string propertyName) =>
        node.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase));
}
