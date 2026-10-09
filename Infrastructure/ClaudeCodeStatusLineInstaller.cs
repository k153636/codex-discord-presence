using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

internal sealed class ClaudeCodeStatusLineInstaller(string settingsPath, string integrationDirectory, string executablePath)
{
    private const string OwnershipMarker = ClaudeCodeNativeCommand.StatusLineOwnershipMarker;
    private bool _installed;
    private string? _lastFailure;
    private string ManifestPath => Path.Combine(integrationDirectory, "status-line-owner.json");

    internal bool Sync(bool enabled, DiagnosticLog log)
    {
        try
        {
            if (enabled && !_installed)
            {
                Install();
                _installed = true;
                _lastFailure = null;
                log.Info("Claude Code status line integration: installed (existing display preserved).");
            }
            else if (!enabled && _installed)
            {
                Uninstall();
                _installed = false;
                _lastFailure = null;
                log.Info("Claude Code status line integration: restored.");
            }
            return enabled && _installed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            if (_lastFailure != ex.Message)
            {
                log.Warn($"Claude Code status line integration unavailable: {ex.Message}");
                _lastFailure = ex.Message;
            }
            return false;
        }
    }

    internal void Install() => UpdateSettings(true);
    internal void Uninstall() => UpdateSettings(false);

    private void UpdateSettings(bool install)
    {
        Directory.CreateDirectory(integrationDirectory);
        using var mutex = new Mutex(false, "Local\\CodexDiscordPresence.ClaudeCode.Settings");
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Claude settings are busy.");
            UpdateSettingsUnderLock(install);
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    private void UpdateSettingsUnderLock(bool install)
    {
        var originalText = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var root = originalText is null ? new JsonObject() : JsonNode.Parse(originalText) as JsonObject
            ?? throw new InvalidOperationException("Claude settings must be a JSON object.");
        var present = root.ContainsKey("statusLine");
        var current = root["statusLine"];
        var manifest = File.Exists(ManifestPath) ? ReadManifest(ManifestPath) : null;
        if (!install && manifest is null)
        {
            if (IsAppCommand(current)) throw new InvalidOperationException("Claude status line ownership is missing; settings preserved.");
            return;
        }
        if (manifest is null)
        {
            ValidateOriginal(current);
            if (IsAppCommand(current)) throw new InvalidOperationException("An unowned Claude status line exists; settings preserved.");
            manifest = new JsonObject
            {
                ["owner"] = OwnershipMarker,
                ["originalPresent"] = present,
                ["original"] = current?.DeepClone(),
                ["definitions"] = new JsonArray()
            };
        }
        var original = manifest["original"];
        var originalPresent = manifest["originalPresent"]!.GetValue<bool>();
        var definitions = (JsonArray)manifest["definitions"]!;
        var currentIsOwned = definitions.Any(item => JsonNode.DeepEquals(item, current));
        var currentIsOriginal = present == originalPresent && JsonNode.DeepEquals(current, original);
        if (!currentIsOwned && !currentIsOriginal)
            throw new InvalidOperationException("The Claude status line was changed; settings preserved.");

        if (install)
        {
            var definition = original?.DeepClone() as JsonObject ?? new JsonObject();
            definition["type"] = "command";
            definition["command"] = CreateCommand(executablePath);
            if (JsonNode.DeepEquals(current, definition)) return;
            if (!definitions.Any(item => JsonNode.DeepEquals(item, definition))) definitions.Add(definition.DeepClone());
            CheckUnchanged(originalText);
            // Write ownership before replacing settings so interrupted installation remains recoverable.
            AtomicWrite(ManifestPath, manifest.ToJsonString());
            root["statusLine"] = definition;
        }
        else
        {
            if (originalPresent) root["statusLine"] = original?.DeepClone();
            else root.Remove("statusLine");
        }
        var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (!JsonNode.DeepEquals(JsonNode.Parse(originalText ?? "{}"), root))
        {
            if (originalText is not null)
                File.WriteAllText(Path.Combine(integrationDirectory, $"settings-backup-{Guid.NewGuid():N}.json"), originalText);
            CheckUnchanged(originalText);
            AtomicWrite(settingsPath, updated);
        }
        if (install)
        {
            manifest["definitions"] = new JsonArray(root["statusLine"]!.DeepClone());
            AtomicWrite(ManifestPath, manifest.ToJsonString());
        }
        else File.Delete(ManifestPath);
    }

    internal static string? ReadOriginalCommand(string manifestPath)
    {
        var manifest = ReadManifest(manifestPath);
        return (manifest["original"] as JsonObject)?["command"]?.GetValue<string>();
    }

    private static JsonObject ReadManifest(string path)
    {
        if (new FileInfo(path).Length > 131_072) throw new InvalidOperationException("Claude status line ownership is invalid.");
        var manifest = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
        if (manifest?["owner"]?.GetValue<string>() != OwnershipMarker ||
            manifest["originalPresent"] is not JsonValue present || !present.TryGetValue<bool>(out _) ||
            manifest["definitions"] is not JsonArray definitions || definitions.Count > 64 ||
            definitions.Any(item => item is not JsonObject || !IsAppCommand(item)))
            throw new InvalidOperationException("Claude status line ownership is invalid.");
        ValidateOriginal(manifest["original"]);
        if (IsAppCommand(manifest["original"])) throw new InvalidOperationException("Claude status line ownership is recursive.");
        return manifest;
    }

    private static void ValidateOriginal(JsonNode? original)
    {
        if (original is null) return;
        if (original is not JsonObject obj || obj["command"] is not JsonValue command ||
            !command.TryGetValue<string>(out var text) || string.IsNullOrWhiteSpace(text) || text.Length > 16_384 ||
            obj["type"] is { } type && (type is not JsonValue value || !value.TryGetValue<string>(out var name) || name != "command"))
            throw new InvalidOperationException("Claude status line has an unsupported shape; settings preserved.");
    }

    private void CheckUnchanged(string? original)
    {
        if ((File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null) != original)
            throw new IOException("Claude settings changed concurrently; installation deferred.");
    }

    internal static string CreateCommand(string executablePath) =>
        ClaudeCodeNativeCommand.Create(executablePath, "--claude-statusline");

    private static bool IsAppCommand(JsonNode? node)
    {
        if (node is not JsonObject obj || obj["command"] is not JsonValue value || !value.TryGetValue<string>(out var command)) return false;
        var separator = command.LastIndexOf(" -EncodedCommand ", StringComparison.Ordinal);
        if (separator < 0) return false;
        try { return Encoding.Unicode.GetString(Convert.FromBase64String(command[(separator + 17)..])).StartsWith("# " + OwnershipMarker, StringComparison.Ordinal); }
        catch (FormatException) { return false; }
    }

    private static void AtomicWrite(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents, new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
