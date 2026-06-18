using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class AppDataInitializerTests
{
    [Fact]
    public void EnsureInitialized_CreatesAppDataDirectoriesAndUserSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexAppDataTests_" + Guid.NewGuid());
        var exeDir = Path.Combine(root, "exe");
        var appDataDir = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exeDir);

        try
        {
            var paths = new AppPaths(
                exeDir,
                Path.Combine(exeDir, "appsettings.json"),
                appDataDir,
                Path.Combine(appDataDir, "logs"),
                Path.Combine(appDataDir, "user-settings.json"),
                Path.Combine(appDataDir, "presence-state.json"));

            AppDataInitializer.EnsureInitialized(paths);

            Assert.True(Directory.Exists(appDataDir));
            Assert.True(Directory.Exists(paths.LogsDirectory));
            Assert.True(File.Exists(paths.UserSettingsPath));
            Assert.Contains("EnableUpdateCheck", File.ReadAllText(paths.UserSettingsPath));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
