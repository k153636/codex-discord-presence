using System.Text;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence.Tests;

public sealed class ClaudeCodeStatusLineTests
{
    [Fact]
    public void Installer_RoundTripPreservesDisplayOptionsOtherSettingsAndEdits_AndIsIdempotent()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            var original = JsonNode.Parse("{\"model\":\"custom\",\"statusLine\":{\"type\":\"command\",\"command\":\"node user-status.mjs\",\"padding\":2,\"refreshInterval\":30},\"hooks\":{\"Stop\":[]}}")!;
            File.WriteAllText(path, original.ToJsonString());
            var installer = Installer(directory);
            installer.Install();
            var installed = File.ReadAllText(path);
            installer.Install();
            Assert.Equal(installed, File.ReadAllText(path));
            var json = JsonNode.Parse(installed)!;
            Assert.Equal(2, json["statusLine"]!["padding"]!.GetValue<int>());
            Assert.Equal(30, json["statusLine"]!["refreshInterval"]!.GetValue<int>());
            Assert.Equal("node user-status.mjs", ClaudeCodeStatusLineInstaller.ReadOriginalCommand(Manifest(directory)));
            json["model"] = "later-user-edit";
            File.WriteAllText(path, json.ToJsonString());
            installer.Uninstall();
            original["model"] = "later-user-edit";
            Assert.True(JsonNode.DeepEquals(original, JsonNode.Parse(File.ReadAllText(path))));
            Assert.False(File.Exists(Manifest(directory)));
        });
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"statusLine\":null}")]
    public void Installer_AbsentOrNullOriginal_RestoresItsExactShape(string original)
    {
        InDirectory(directory =>
        {
            File.WriteAllText(Path.Combine(directory, "settings.json"), original);
            var installer = Installer(directory);
            installer.Install();
            Assert.Null(ClaudeCodeStatusLineInstaller.ReadOriginalCommand(Manifest(directory)));
            installer.Uninstall();
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")))));
        });
    }

    [Fact]
    public void Installer_ChangedOwnedCommandOrMissingManifest_FailsWithoutOverwriting()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, "{}");
            var installer = Installer(directory);
            installer.Install();
            var installed = File.ReadAllText(path);
            var changed = JsonNode.Parse(installed)!;
            changed["statusLine"]!["padding"] = 5;
            File.WriteAllText(path, changed.ToJsonString());
            var originalText = File.ReadAllText(path);
            Assert.Throws<InvalidOperationException>(() => installer.Uninstall());
            Assert.Throws<InvalidOperationException>(() => installer.Install());
            Assert.Equal(originalText, File.ReadAllText(path));
            File.WriteAllText(path, installed);
            File.Delete(Manifest(directory));
            Assert.Throws<InvalidOperationException>(() => installer.Install());
            Assert.Equal(installed, File.ReadAllText(path));
        });
    }

    [Theory]
    [InlineData("{\"statusLine\":\"user command\"}")]
    [InlineData("{\"statusLine\":{\"type\":\"prompt\",\"command\":\"user command\"}}")]
    [InlineData("{\"statusLine\":{\"type\":\"command\",\"command\":42}}")]
    [InlineData("[]")]
    public void Installer_UnsupportedSettings_ArePreserved(string original)
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            File.WriteAllText(path, original);
            Assert.Throws<InvalidOperationException>(() => Installer(directory).Install());
            Assert.Equal(original, File.ReadAllText(path));
        });
    }

    [Fact]
    public void Installer_ExecutableUpgradeAndInterruptedInstall_KeepOriginalRestorable()
    {
        InDirectory(directory =>
        {
            var path = Path.Combine(directory, "settings.json");
            var original = "{\"statusLine\":{\"type\":\"command\",\"command\":\"user-command\"}}";
            File.WriteAllText(path, original);
            Installer(directory).Install();
            var upgrade = new ClaudeCodeStatusLineInstaller(path, Path.Combine(directory, "owned"), "C:/new/rpc.exe");
            upgrade.Install();
            Assert.Equal("user-command", ClaudeCodeStatusLineInstaller.ReadOriginalCommand(Manifest(directory)));
            // A prepared ownership manifest may exist while settings still contain the original.
            File.WriteAllText(path, original);
            upgrade.Install();
            upgrade.Uninstall();
            Assert.True(JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(File.ReadAllText(path))));
        });
    }

    [Fact]
    public async Task Relay_OriginalCommandReceivesIdenticalInput_AndPreservesUnicodeAnsiOutputStderrAndExitCode()
    {
        var script = "[Console]::OpenStandardInput().CopyTo([Console]::OpenStandardOutput()); [Console]::Error.Write('original stderr'); exit 7";
        var command = "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var payload = Encoding.UTF8.GetBytes("{\"session_id\":\"日本語\"}\n\u001b[32m既存表示\u001b[0m\n");
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        var exitCode = await ClaudeCodeStatusLineCommand.RelayAsync(command, payload, output, error);
        Assert.Equal(7, exitCode);
        Assert.Equal(payload, output.ToArray());
        Assert.Equal("original stderr", Encoding.UTF8.GetString(error.ToArray()));
    }

    [Fact]
    public async Task Relay_OriginalCommandDoesNotReadStdin_StillPreservesItsOutputAndExitCode()
    {
        var script = "[Console]::Out.Write('existing display'); exit 7";
        var command = "powershell.exe -NoLogo -NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        using var output = new MemoryStream();
        using var error = new MemoryStream();
        Assert.Equal(7, await ClaudeCodeStatusLineCommand.RelayAsync(command, new byte[262_144], output, error));
        Assert.Equal("existing display", Encoding.UTF8.GetString(output.ToArray()));
    }

    [Fact]
    public async Task GeneratedBridge_DoesNotAppendPowerShellTaskResultsOrProgress_ToNativeOutput()
    {
        var executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "whoami.exe");
        var directCommand = '"' + executable.Replace('\\', '/') + "\" --claude-statusline";
        using var directOutput = new MemoryStream();
        using var directError = new MemoryStream();
        var directExit = await ClaudeCodeStatusLineCommand.RelayAsync(directCommand, [], directOutput, directError);
        using var wrappedOutput = new MemoryStream();
        using var wrappedError = new MemoryStream();
        var wrappedExit = await ClaudeCodeStatusLineCommand.RelayAsync(
            ClaudeCodeStatusLineInstaller.CreateCommand(executable), [], wrappedOutput, wrappedError);
        Assert.Equal(directExit, wrappedExit);
        Assert.Equal(directOutput.ToArray(), wrappedOutput.ToArray());
        Assert.Equal(directError.ToArray(), wrappedError.ToArray());
    }

    private static ClaudeCodeStatusLineInstaller Installer(string directory) => new(Path.Combine(directory, "settings.json"), Path.Combine(directory, "owned"), "C:/test/rpc.exe");
    private static string Manifest(string directory) => Path.Combine(directory, "owned", "status-line-owner.json");
    private static void InDirectory(Action<string> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ClaudeStatusLineTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(directory); }
        finally { Directory.Delete(directory, true); }
    }
}
