using System.Text.Json.Nodes;

namespace CodexDiscordPresence.Tests;

public sealed class UpdateSettingsPreserverTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rpc-settings-tests-" + Guid.NewGuid());
    private readonly AppPaths _paths;

    public UpdateSettingsPreserverTests()
    {
        var executable = Path.Combine(_directory, "current");
        var data = Path.Combine(_directory, "data");
        Directory.CreateDirectory(executable);
        Directory.CreateDirectory(data);
        _paths = new AppPaths(executable, Path.Combine(executable, "appsettings.json"), data,
            Path.Combine(data, "logs"), Path.Combine(data, "user-settings.json"),
            Path.Combine(data, "presence-state.json"), AppProfileKind.Codex);
        Write("appsettings.json", "{}");
        Write("appsettings.cli.json", "{}");
        Write("appsettings.defaults.json", "{}");
        Write("appsettings.cli.defaults.json", "{}");
    }

    [Fact]
    public void Preserve_RetainsOnlyOverridesAndBacksUpTheOriginalSettingsAndState()
    {
        Write("appsettings.defaults.json", """{"Discord":{"ClientId":"default","Enabled":true},"UpdateIntervalSeconds":2}""");
        Write("appsettings.json", """{"Discord":{"ClientId":"custom","Enabled":true},"UpdateIntervalSeconds":2}""");
        Write("appsettings.cli.defaults.json", """{"DiscordCli":{"ClientId":"cli-default"}}""");
        Write("appsettings.cli.json", """{"DiscordCli":{"ClientId":"cli-custom"}}""");
        File.WriteAllText(_paths.UserSettingsPath, """{"Discord":{"ClientId":"persistent"},"CustomFutureSetting":7}""");
        File.WriteAllText(_paths.StatePath, """{"Enabled":false,"Providers":{"claude-code":true}}""");
        new UpdateSettingsPreserver(_paths).Preserve();
        var result = JsonNode.Parse(File.ReadAllText(_paths.UserSettingsPath))!.AsObject();
        Assert.Equal("persistent", result["Discord"]!["ClientId"]!.GetValue<string>());
        Assert.Equal("cli-custom", result["DiscordCli"]!["ClientId"]!.GetValue<string>());
        Assert.Equal(7, result["CustomFutureSetting"]!.GetValue<int>());
        Assert.Null(result["Discord"]!["Enabled"]);
        Assert.Null(result["UpdateIntervalSeconds"]);
        var backup = Assert.Single(Directory.GetDirectories(Path.Combine(_paths.AppDataDirectory, "update-backups")));
        Assert.Equal("custom", JsonNode.Parse(File.ReadAllText(Path.Combine(backup, "appsettings.json")))!["Discord"]!["ClientId"]!.GetValue<string>());
        Assert.Equal(File.ReadAllText(_paths.StatePath), File.ReadAllText(Path.Combine(backup, "presence-state.json")));
        Assert.Empty(Directory.GetFiles(_paths.AppDataDirectory, "*.tmp"));
    }

    [Fact]
    public void Preserve_HandlesCaseInsensitiveKeysCommentsAndArrays()
    {
        Write("appsettings.defaults.json", """{"Discord":{"ClientId":"default"},"List":[1]}""");
        Write("appsettings.json", """{/* override */ "discord":{"clientid":"custom"},"List":[2],}""");
        File.WriteAllText(_paths.UserSettingsPath, """{"DISCORD":{"CLIENTID":"user"}}""");
        new UpdateSettingsPreserver(_paths).Preserve();
        var result = JsonNode.Parse(File.ReadAllText(_paths.UserSettingsPath))!.AsObject();
        Assert.Single(result, pair => pair.Key.Equals("discord", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("user", result["discord"]!["clientid"]!.GetValue<string>());
        Assert.Equal(2, result["List"]![0]!.GetValue<int>());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("[]")]
    public void Preserve_InvalidUserSettingsLeavesAllOriginalFilesIntact(string invalid)
    {
        File.WriteAllText(_paths.UserSettingsPath, invalid);
        Assert.ThrowsAny<Exception>(() => new UpdateSettingsPreserver(_paths).Preserve());
        Assert.Equal(invalid, File.ReadAllText(_paths.UserSettingsPath));
        Assert.Equal("{}", File.ReadAllText(_paths.ExecutableSettingsPath));
        Assert.False(Directory.Exists(Path.Combine(_paths.AppDataDirectory, "update-backups")));
    }

    private void Write(string name, string contents) => File.WriteAllText(Path.Combine(_paths.BaseDirectory, name), contents);
    public void Dispose() => Directory.Delete(_directory, recursive: true);
}
