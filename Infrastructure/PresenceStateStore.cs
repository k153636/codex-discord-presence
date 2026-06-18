using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class PresenceStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static string GetDefaultPath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "CodexDiscordPresence", "presence-state.json");
    }

    public PresenceRuntimeState Load(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new PresenceRuntimeState();
            }

            var json = File.ReadAllText(path);
            var state = JsonSerializer.Deserialize<PresenceStateDto>(json, JsonOptions);
            return new PresenceRuntimeState
            {
                Enabled = state?.Enabled ?? true
            };
        }
        catch
        {
            return new PresenceRuntimeState();
        }
    }

    public void Save(string path, PresenceRuntimeState state)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var dto = new PresenceStateDto(state.Enabled);
            File.WriteAllText(path, JsonSerializer.Serialize(dto, JsonOptions));
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to save presence state: {ex.Message}");
        }
    }

    private sealed record PresenceStateDto(bool Enabled);
}
