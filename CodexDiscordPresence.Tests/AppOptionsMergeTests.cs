using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class AppOptionsMergeTests
{
    [Fact]
    public void Load_MergesUserSettingsOverExecutableSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexAppOptionsTests_" + Guid.NewGuid());
        var exeDir = Path.Combine(root, "exe");
        var appDataDir = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(appDataDir);

        try
        {
            File.WriteAllText(
                Path.Combine(exeDir, "appsettings.json"),
                """
{
  "EnableUpdateCheck": true,
  "UpdateIntervalSeconds": 9,
  "Presence": {
    "ActiveUpdateIntervalSeconds": 7,
    "ModelName": "FromExe"
  }
}
""");

            File.WriteAllText(
                Path.Combine(appDataDir, "user-settings.json"),
                """
{
  "EnableUpdateCheck": false,
  "Presence": {
    "ActiveUpdateIntervalSeconds": 3
  }
}
""");

            var paths = new AppPaths(
                exeDir,
                Path.Combine(exeDir, "appsettings.json"),
                appDataDir,
                Path.Combine(appDataDir, "logs"),
                Path.Combine(appDataDir, "user-settings.json"),
                Path.Combine(appDataDir, "presence-state.json"));

            var options = AppOptions.Load(Array.Empty<string>(), paths);

            Assert.False(options.EnableUpdateCheck);
            Assert.Equal(9, options.UpdateIntervalSeconds);
            Assert.Equal(3, options.Presence.ActiveUpdateIntervalSeconds);
            Assert.Equal("FromExe", options.Presence.ModelName);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
