namespace CodexDiscordPresence;

public static class AppDataInitializer
{
    private const string DefaultUserSettingsJson = """
{
  "EnableUpdateCheck": true
}
""";

    public static void EnsureInitialized(AppPaths paths)
    {
        EnsureDirectory(paths.AppDataDirectory, "AppData");
        EnsureDirectory(paths.LogsDirectory, "logs");
        EnsureUserSettings(paths.UserSettingsPath);
    }

    private static void EnsureDirectory(string path, string label)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create {label} directory '{path}': {ex.Message}");
        }
    }

    private static void EnsureUserSettings(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return;
            }

            File.WriteAllText(path, DefaultUserSettingsJson);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create user settings '{path}': {ex.Message}");
        }
    }
}
