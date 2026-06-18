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
                Path.Combine(appDataDir, "presence-state.json"),
                AppProfileKind.Codex);

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

    [Fact]
    public void Load_UsesCliSettingsFilesForCliProfile()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexAppOptionsCliTests_" + Guid.NewGuid());
        var exeDir = Path.Combine(root, "exe");
        var appDataDir = Path.Combine(root, "appdata");
        Directory.CreateDirectory(exeDir);
        Directory.CreateDirectory(appDataDir);

        try
        {
            File.WriteAllText(
                Path.Combine(exeDir, "appsettings.cli.json"),
                """
{
  "CodexCli": {
    "ProcessNameContains": [],
    "WindowTitleContains": [],
    "ExecutablePathContains": [],
    "CommandLineContains": [ "@openai/codex/bin/codex.js", "@openai\\codex\\bin\\codex.js" ]
  },
  "DiscordCli": {
    "ClientId": "1516846793873424474",
    "LargeImageKey": "codexcli_logo1"
  },
  "Presence": {}
}
""");

            File.WriteAllText(
                Path.Combine(appDataDir, "user-settings.cli.json"),
                """
{
  "UpdateIntervalSeconds": 4
}
""");

            var paths = new AppPaths(
                exeDir,
                Path.Combine(exeDir, "appsettings.cli.json"),
                appDataDir,
                Path.Combine(appDataDir, "logs"),
                Path.Combine(appDataDir, "user-settings.cli.json"),
                Path.Combine(appDataDir, "presence-state.json"),
                AppProfileKind.CodexCli);

            var options = AppOptions.Load(Array.Empty<string>(), paths);

            Assert.Equal("1516846793873424474", options.Discord.ClientId);
            Assert.NotNull(options.DiscordCli);
            Assert.Equal("1516846793873424474", options.DiscordCli!.ClientId);
            Assert.Equal("codexcli_logo1", options.DiscordCli.LargeImageKey);
            Assert.Equal("{GoalModePrefix} {ModelName} \u2022 {Tokens}", options.Presence.Details);
            Assert.Equal("Working", options.Presence.WorkingText);
            Assert.Equal(4, options.UpdateIntervalSeconds);
            Assert.NotNull(options.CodexCli);
            Assert.Contains("codex", options.Codex.ProcessNameContains);
            Assert.Empty(options.CodexCli!.ProcessNameContains);
            Assert.Empty(options.CodexCli.WindowTitleContains);
            Assert.Empty(options.CodexCli.ExecutablePathContains);
            Assert.Contains("@openai/codex/bin/codex.js", options.CodexCli.CommandLineContains);
            Assert.Contains("@openai\\codex\\bin\\codex.js", options.CodexCli.CommandLineContains);
            Assert.Same(options.CodexCli, options.GetCodexDetectionOptions(AppProfileKind.CodexCli));
            Assert.Same(options.DiscordCli, options.GetDiscordOptions(AppProfileKind.CodexCli));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
