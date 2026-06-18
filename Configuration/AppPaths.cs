namespace CodexDiscordPresence;

public enum AppProfileKind
{
    Codex,
    CodexCli
}

public sealed record AppPaths(
    string BaseDirectory,
    string ExecutableSettingsPath,
    string AppDataDirectory,
    string LogsDirectory,
    string UserSettingsPath,
    string StatePath,
    AppProfileKind Profile)
{
    public static AppPaths Create(AppProfileKind profile, string? baseDirectory = null)
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
            Path.Combine(appDataDirectory, "presence-state.json"),
            profile);
    }
}
