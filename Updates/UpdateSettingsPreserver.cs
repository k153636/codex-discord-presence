using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

internal sealed class UpdateSettingsPreserver(AppPaths paths)
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public void Preserve()
    {
        // Compare editable files with packaged baselines so new defaults can still evolve.
        var overrides = new JsonObject();
        foreach (var name in new[] { "appsettings.json", "appsettings.cli.json" })
        {
            var baseline = ReadObject(Path.Combine(paths.BaseDirectory, name.Replace(".json", ".defaults.json")));
            var current = ReadObject(Path.Combine(paths.BaseDirectory, name));
            Merge(overrides, Differences(current, baseline));
        }
        var user = File.Exists(paths.UserSettingsPath) ? ReadObject(paths.UserSettingsPath) : new JsonObject();
        Merge(overrides, user); // Existing per-user settings retain their precedence.

        var backup = Path.Combine(paths.AppDataDirectory, "update-backups",
            DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff") + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(backup);
        foreach (var file in new[] { paths.ExecutableSettingsPath,
                     Path.Combine(paths.BaseDirectory, "appsettings.cli.json"), paths.UserSettingsPath, paths.StatePath })
        {
            if (File.Exists(file)) File.Copy(file, Path.Combine(backup, Path.GetFileName(file)));
        }

        var temporary = paths.UserSettingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, overrides.ToJsonString(JsonOptions));
            File.Move(temporary, paths.UserSettingsPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static JsonObject ReadObject(string path) =>
        JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip
        }) as JsonObject ?? throw new InvalidDataException($"Settings must be a JSON object: {Path.GetFileName(path)}");

    private static JsonObject Differences(JsonObject current, JsonObject baseline)
    {
        var result = new JsonObject();
        foreach (var (key, value) in current)
        {
            var baselineKey = FindKey(baseline, key);
            var original = baselineKey is null ? null : baseline[baselineKey];
            if (value is JsonObject nested && original is JsonObject originalNested)
            {
                var difference = Differences(nested, originalNested);
                if (difference.Count > 0) result[key] = difference;
            }
            else if (!JsonNode.DeepEquals(value, original) || baselineKey is null)
            {
                result[key] = value?.DeepClone();
            }
        }
        return result;
    }

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            var targetKey = FindKey(target, key) ?? key;
            if (value is JsonObject nested && target[targetKey] is JsonObject existing) Merge(existing, nested);
            else target[targetKey] = value?.DeepClone();
        }
    }

    private static string? FindKey(JsonObject node, string key) => node.Select(pair => pair.Key)
        .FirstOrDefault(candidate => string.Equals(candidate, key, StringComparison.OrdinalIgnoreCase));
}
