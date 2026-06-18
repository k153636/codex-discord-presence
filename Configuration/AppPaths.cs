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
            profile == AppProfileKind.CodexCli
                ? "CodexDiscordPresenceCli"
                : "CodexDiscordPresence");

        var settingsFileName = profile == AppProfileKind.CodexCli
            ? "appsettings.cli.json"
            : "appsettings.json";

        var userSettingsFileName = profile == AppProfileKind.CodexCli
            ? "user-settings.cli.json"
            : "user-settings.json";

        return new AppPaths(
            Path.GetFullPath(baseDirectory),
            Path.Combine(Path.GetFullPath(baseDirectory), settingsFileName),
            appDataDirectory,
            Path.Combine(appDataDirectory, "logs"),
            Path.Combine(appDataDirectory, userSettingsFileName),
            Path.Combine(appDataDirectory, "presence-state.json"),
            profile);
    }
}
