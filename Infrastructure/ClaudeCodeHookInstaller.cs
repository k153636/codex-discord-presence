using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

internal sealed class ClaudeCodeHookInstaller(string settingsPath, string integrationDirectory, string executablePath)
{
    private const string OwnershipMarker = "CodexDiscordPresence.ClaudeCodeHook.v1";
    private bool _installed;
    private string? _lastFailure;

    internal bool Sync(bool enabled, DiagnosticLog log)
    {
        try
        {
            if (enabled && !_installed)
            {
                Install();
                _installed = true;
                _lastFailure = null;
                log.Info("Claude Code hooks integration: installed (existing hooks preserved).");
            }
            else if (!enabled && _installed)
            {
                Uninstall();
                _installed = false;
                _lastFailure = null;
                log.Info("Claude Code hooks integration: removed.");
            }
            return enabled && _installed;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            if (_lastFailure != ex.Message)
            {
                log.Warn($"Claude Code hooks integration unavailable: {ex.Message}");
                _lastFailure = ex.Message;
            }
            return false;
        }
    }

    internal void Install() => UpdateSettings(install: true);
    internal void Uninstall() => UpdateSettings(install: false);

    private void UpdateSettings(bool install)
    {
        Directory.CreateDirectory(integrationDirectory);
        using var mutex = new Mutex(false, "Local\\CodexDiscordPresence.ClaudeCode.Settings");
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
            {
                throw new IOException("Claude settings are busy.");
            }
            UpdateSettingsUnderLock(install);
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    private void UpdateSettingsUnderLock(bool install)
    {
        var original = File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
        var root = original is null ? new JsonObject() : JsonNode.Parse(original) as JsonObject
            ?? throw new InvalidOperationException("Claude settings must be a JSON object.");
        if (root["hooks"] is not null && root["hooks"] is not JsonObject)
        {
            throw new InvalidOperationException("Claude hooks have an unsupported shape; settings preserved.");
        }
        var hooks = root["hooks"] as JsonObject ?? new JsonObject();
        var manifestPath = Path.Combine(integrationDirectory, "hook-owner.json");
        var definition = CreateDefinition(executablePath);
        JsonNode? ownedDefinition = null;
        if (File.Exists(manifestPath))
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath)) as JsonObject;
            if (manifest?["owner"]?.GetValue<string>() != OwnershipMarker || manifest["definition"] is not JsonObject)
            {
                throw new InvalidOperationException("Claude hook ownership cannot be verified; settings preserved.");
            }
            ownedDefinition = manifest["definition"];
        }

        foreach (var name in ClaudeCodeHookParser.EventNames)
        {
            if (hooks[name] is not null && hooks[name] is not JsonArray)
            {
                throw new InvalidOperationException($"Claude hook {name} has an unsupported shape; settings preserved.");
            }
            var groups = hooks[name] as JsonArray ?? new JsonArray();
            foreach (var group in groups)
            {
                if (IsAppDefinition(group) &&
                    (ownedDefinition is null || !JsonNode.DeepEquals(group, ownedDefinition)))
                {
                    throw new InvalidOperationException("An app hook was changed or is unowned; settings preserved.");
                }
            }
            for (var index = groups.Count - 1; index >= 0; index--)
            {
                if (ownedDefinition is not null && JsonNode.DeepEquals(groups[index], ownedDefinition))
                {
                    groups.RemoveAt(index);
                }
            }
            if (install)
            {
                groups.Add(definition.DeepClone());
            }
            if (groups.Count > 0)
            {
                hooks[name] = groups;
            }
            else
            {
                hooks.Remove(name);
            }
        }
        if (hooks.Count > 0)
        {
            root["hooks"] = hooks;
        }
        else
        {
            root.Remove("hooks");
        }
        var updated = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        if (original is not null && JsonNode.DeepEquals(JsonNode.Parse(original), root))
        {
            return;
        }
        if (original is not null)
        {
            File.WriteAllText(Path.Combine(integrationDirectory, $"settings-backup-{Guid.NewGuid():N}.json"), original);
        }
        // Establish ownership first so an interrupted installation remains recoverable.
        if (install)
        {
            AtomicWrite(manifestPath, new JsonObject
            {
                ["owner"] = OwnershipMarker,
                ["definition"] = definition.DeepClone()
            }.ToJsonString());
        }
        if ((File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null) != original)
        {
            throw new IOException("Claude settings changed concurrently; installation deferred.");
        }
        AtomicWrite(settingsPath, updated);
        if (!install && File.Exists(manifestPath))
        {
            File.Delete(manifestPath);
        }
    }

    internal static JsonObject CreateDefinition(string executablePath)
    {
        var escapedPath = executablePath.Replace("'", "''", StringComparison.Ordinal);
        var script = $"# {OwnershipMarker}\n$payload = [Console]::In.ReadToEnd(); $payload | & '{escapedPath}' --claude-hook; exit 0";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return new JsonObject
        {
            ["hooks"] = new JsonArray(new JsonObject
            {
                ["type"] = "command",
                ["command"] = "powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encoded,
                ["timeout"] = 5
            })
        };
    }

    private static bool IsAppDefinition(JsonNode? group)
    {
        if (group is not JsonObject obj || obj["hooks"] is not JsonArray commands)
        {
            return false;
        }
        foreach (var command in commands.OfType<JsonObject>())
        {
            if (command["command"] is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                continue;
            }
            var separator = text.LastIndexOf(" -EncodedCommand ", StringComparison.Ordinal);
            if (separator < 0)
            {
                continue;
            }
            try
            {
                var script = Encoding.Unicode.GetString(Convert.FromBase64String(text[(separator + 17)..]));
                if (script.StartsWith("# " + OwnershipMarker + "\n", StringComparison.Ordinal))
                {
                    return true;
                }
            }
            catch (FormatException)
            {
                continue;
            }
        }
        return false;
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
        finally
        {
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
        }
    }
}
