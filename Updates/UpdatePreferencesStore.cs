using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed record UpdatePreferences(bool AutomaticEnabled = true, DateTime? DeferredUntilUtc = null);

internal sealed class UpdatePreferencesStore(string path)
{
    public UpdatePreferences Load() => File.Exists(path)
        ? JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(path))
            ?? throw new InvalidDataException("Update preferences are empty.")
        : new UpdatePreferences();

    public void Save(UpdatePreferences preferences)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
