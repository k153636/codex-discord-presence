using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

public sealed class PresenceStateStore
{
    private const string EnabledPropertyName = "Enabled";
    private const string ProvidersPropertyName = "Providers";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly JsonDocumentOptions JsonDocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "CodexDiscordPresence", "presence-state.json");
    }

    public PresenceRuntimeState Load(string path)
    {
        return Load(path, configuredProviders: null);
    }

    public PresenceRuntimeState Load(
        string path,
        IReadOnlyDictionary<string, ProviderOptions>? configuredProviders)
    {
        var enabledByProvider = GetConfiguredProviderDefaults(configuredProviders);

        try
        {
            if (!File.Exists(path))
            {
                return CreateState(enabled: true, enabledByProvider);
            }

            var root = ParseRoot(File.ReadAllText(path));
            if (root is null)
            {
                return CreateState(enabled: true, enabledByProvider);
            }

            var providers = GetProperty(root, ProvidersPropertyName) as JsonObject;
            var hasCodexProvider = false;
            if (providers is not null)
            {
                foreach (var (providerId, providerValue) in providers)
                {
                    if (!TryReadProviderEnabled(providerValue, out var providerEnabled))
                    {
                        continue;
                    }

                    enabledByProvider[providerId] = providerEnabled;
                    hasCodexProvider |= string.Equals(providerId, ProviderIds.Codex, StringComparison.OrdinalIgnoreCase);
                }
            }

            if (!hasCodexProvider &&
                TryReadBoolean(GetProperty(root, EnabledPropertyName), out var legacyEnabled))
            {
                enabledByProvider[ProviderIds.Codex] = legacyEnabled;
            }

            var enabled = TryReadBoolean(GetProperty(root, EnabledPropertyName), out var persistedEnabled)
                ? persistedEnabled
                : true;
            return CreateState(enabled, enabledByProvider);
        }
        catch
        {
            return CreateState(enabled: true, enabledByProvider);
        }
    }

    public void Save(string path, PresenceRuntimeState state)
    {
        try
        {
            var root = LoadRoot(path);
            SetProperty(root, EnabledPropertyName, JsonValue.Create(state.Enabled));
            MergeProviderStates(root, state.ProviderEnabled);
            WriteAtomically(path, root.ToJsonString(JsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save presence state: {ex.Message}");
        }
    }

    private static PresenceRuntimeState CreateState(
        bool enabled,
        IReadOnlyDictionary<string, bool> enabledByProvider)
    {
        var state = new PresenceRuntimeState { Enabled = enabled };
        state.InitializeProviderEnabled(enabledByProvider);
        return state;
    }

    private static Dictionary<string, bool> GetConfiguredProviderDefaults(
        IReadOnlyDictionary<string, ProviderOptions>? configuredProviders)
    {
        var enabledByProvider = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (configuredProviders is null)
        {
            return enabledByProvider;
        }

        foreach (var (providerId, options) in configuredProviders)
        {
            if (!string.IsNullOrWhiteSpace(providerId) && options is not null)
            {
                enabledByProvider[providerId.Trim()] = options.Enabled;
            }
        }

        return enabledByProvider;
    }

    private static JsonObject LoadRoot(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        return ParseRoot(File.ReadAllText(path)) ?? new JsonObject();
    }

    private static JsonObject? ParseRoot(string json)
    {
        return JsonNode.Parse(json, documentOptions: JsonDocumentOptions) as JsonObject;
    }

    private static void MergeProviderStates(
        JsonObject root,
        IReadOnlyDictionary<string, bool> enabledByProvider)
    {
        if (enabledByProvider.Count == 0)
        {
            return;
        }

        var providers = GetProperty(root, ProvidersPropertyName) as JsonObject;
        if (providers is null)
        {
            providers = new JsonObject();
            SetProperty(root, ProvidersPropertyName, providers);
        }

        foreach (var (providerId, enabled) in enabledByProvider)
        {
            if (string.IsNullOrWhiteSpace(providerId))
            {
                continue;
            }

            var existing = GetProperty(providers, providerId);
            if (existing is JsonObject providerObject)
            {
                SetProperty(providerObject, EnabledPropertyName, JsonValue.Create(enabled));
            }
            else if (existing is JsonValue)
            {
                SetProperty(providers, providerId, JsonValue.Create(enabled));
            }
            else
            {
                SetProperty(
                    providers,
                    providerId,
                    new JsonObject { [EnabledPropertyName] = enabled });
            }
        }
    }

    private static bool TryReadProviderEnabled(JsonNode? providerValue, out bool enabled)
    {
        if (providerValue is JsonObject providerObject)
        {
            return TryReadBoolean(GetProperty(providerObject, EnabledPropertyName), out enabled);
        }

        return TryReadBoolean(providerValue, out enabled);
    }

    private static bool TryReadBoolean(JsonNode? value, out bool result)
    {
        if (value is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out result))
        {
            return true;
        }

        result = false;
        return false;
    }

    private static JsonNode? GetProperty(JsonObject node, string propertyName)
    {
        var actualName = node
            .Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase));
        return actualName is null ? null : node[actualName];
    }

    private static void SetProperty(JsonObject node, string propertyName, JsonNode? value)
    {
        var actualName = node
            .Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase))
            ?? propertyName;
        node[actualName] = value;
    }

    private static void WriteAtomically(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents, new System.Text.UTF8Encoding(false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
