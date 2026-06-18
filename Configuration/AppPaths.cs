namespace CodexDiscordPresence;

public sealed record AppPaths(
    string BaseDirectory,
    string ExecutableSettingsPath,
    string AppDataDirectory,
    string LogsDirectory,
    string UserSettingsPath,
    string StatePath)
{
    public static AppPaths Create(string? baseDirectory = null)
    {
        baseDirectory ??= AppContext.BaseDirectory;
        var appDataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexDiscordPresence");

        return new AppPaths(
            Path.GetFullPath(baseDirectory),
            Path.Combine(Path.GetFullPath(baseDirectory), "appsettings.json"),
            appDataDirectory,
            Path.Combine(appDataDirectory, "logs"),
            Path.Combine(appDataDirectory, "user-settings.json"),
            Path.Combine(appDataDirectory, "presence-state.json"));
    }
}
