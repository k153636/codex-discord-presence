using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class AntigravityHookInfrastructureTests
{
    [Fact]
    public void CommandBuilder_WindowsBuildsOneEncodedCommandPerLifecycleEvent()
    {
        using var fixture = new TemporaryFixture();
        var result = new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);

        Assert.True(result.IsSupported);
        Assert.NotNull(result.Commands);
        Assert.Contains(
            "Global\\CodexDiscordPresence.AntigravityHook",
            result.ScriptContent,
            StringComparison.Ordinal);
        Assert.Equal(AntigravityHookEvents.All.Count, result.Commands!.Count);
        Assert.All(AntigravityHookEvents.All, hookEvent =>
        {
            var command = result.Commands[hookEvent];
            Assert.Contains("-EncodedCommand", command, StringComparison.Ordinal);
            var encoded = command[(command.LastIndexOf(' ') + 1)..];
            var invocation = System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
            Assert.Contains($"'-HookEvent' '{hookEvent}'", invocation, StringComparison.Ordinal);
            Assert.Contains(fixture.Paths.ScriptPath.Replace("'", "''", StringComparison.Ordinal), invocation, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CommandBuilder_WindowsScript_MapsOfficialLifecycleEventsToObservations()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent!);

        foreach (var (hookEvent, expectedState) in new Dictionary<string, ProviderAgentState>
        {
            [AntigravityHookEvents.PreInvocation] = ProviderAgentState.Thinking,
            [AntigravityHookEvents.PreToolUse] = ProviderAgentState.ToolUse,
            [AntigravityHookEvents.PostToolUse] = ProviderAgentState.Working,
            [AntigravityHookEvents.PostInvocation] = ProviderAgentState.Idle,
            [AntigravityHookEvents.Stop] = ProviderAgentState.Idle
        })
        {
            var execution = ExecutePowerShellScript(
                fixture.Paths.ScriptPath,
                hookEvent,
                OfficialHookPayload);

            Assert.Equal(0, execution.ExitCode);
            Assert.Equal(
                hookEvent == AntigravityHookEvents.Stop ? "{\"decision\":\"allow\"}" : "{}",
                execution.Output.Trim());
            var store = new AntigravityStatusLineEventStore(fixture.Paths.EventFilePath);
            Assert.True(store.TryReadLatest(@"C:\repo", out var observation));
            Assert.Equal(expectedState, observation?.AgentState);
            Assert.Equal("conversation-id", observation?.ConversationId);
            Assert.Equal("Claude Sonnet 4.6 (Thinking)", observation?.Model?.Id);
        }
    }

    [Fact]
    public void CommandBuilder_WindowsScript_ExtractsOfficialToolCallPayload()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent!);

        var execution = ExecutePowerShellScript(
            fixture.Paths.ScriptPath,
            AntigravityHookEvents.PreToolUse,
            OfficialToolHookPayload);

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("{}", execution.Output.Trim());
        var store = new AntigravityStatusLineEventStore(fixture.Paths.EventFilePath);
        Assert.True(store.TryReadLatest(@"C:\repo", out var observation));
        Assert.Equal(ProviderAgentState.ToolUse, observation?.AgentState);
        Assert.Equal("read_file", observation?.Operation?.ToolName);
        Assert.Equal(@"C:\repo\README.md", observation?.Operation?.TargetPath);
        Assert.False(observation?.Operation?.IsCompleted);
    }

    [Fact]
    public void CommandBuilder_WindowsScript_MissingConversationIdFailsOpenWithoutWriting()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent!);

        var execution = ExecutePowerShellScript(
            fixture.Paths.ScriptPath,
            AntigravityHookEvents.PreInvocation,
            "{\"workspacePaths\":[\"C:\\\\repo\"],\"modelName\":\"model\"}");

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("{}", execution.Output.Trim());
        Assert.False(File.Exists(fixture.Paths.EventFilePath));
    }

    [Fact]
    public void Installer_PreservesUnrelatedHooksAndRemovesOnlyOwnedGroup()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.Paths.SettingsPath,
            "{\"user-hook\":{\"enabled\":false,\"PostInvocation\":[]},\"future\":true}");
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityHookOperationStatus.Installed, installer.Install().Status);
        Assert.True(File.Exists(fixture.Paths.ScriptPath));
        Assert.True(File.Exists(fixture.Paths.OwnershipPath));
        Assert.Equal(
            AntigravityHookOperationStatus.AlreadyInstalled,
            installer.Install().Status);

        var installed = JsonNode.Parse(File.ReadAllText(fixture.Paths.SettingsPath))!.AsObject();
        Assert.NotNull(installed["user-hook"]);
        Assert.True(installed["future"]!.GetValue<bool>());
        var owned = Assert.IsType<JsonObject>(installed["codex-discord-presence"]);
        Assert.NotNull(owned["PreInvocation"]);
        Assert.NotNull(owned["PreToolUse"]);
        Assert.NotNull(owned["PostToolUse"]);
        Assert.NotNull(owned["PostInvocation"]);
        Assert.NotNull(owned["Stop"]);

        Assert.Equal(AntigravityHookOperationStatus.NotInstalled, installer.Uninstall().Status);
        var uninstalled = JsonNode.Parse(File.ReadAllText(fixture.Paths.SettingsPath))!.AsObject();
        Assert.NotNull(uninstalled["user-hook"]);
        Assert.True(uninstalled["future"]!.GetValue<bool>());
        Assert.Null(uninstalled["codex-discord-presence"]);
        Assert.False(File.Exists(fixture.Paths.ScriptPath));
        Assert.False(File.Exists(fixture.Paths.OwnershipPath));
        Assert.False(File.Exists(fixture.Paths.EventFilePath));
    }

    [Fact]
    public void Installer_ExistingHookGroupReturnsConflictWithoutOverwritingIt()
    {
        using var fixture = new TemporaryFixture();
        const string existing = "{\"codex-discord-presence\":{\"PreInvocation\":[]}}";
        File.WriteAllText(fixture.Paths.SettingsPath, existing);
        var installer = fixture.CreateInstaller();

        var result = installer.Install();

        Assert.Equal(AntigravityHookOperationStatus.Conflict, result.Status);
        Assert.Equal(existing, File.ReadAllText(fixture.Paths.SettingsPath));
        Assert.False(File.Exists(fixture.Paths.ScriptPath));
        Assert.False(File.Exists(fixture.Paths.OwnershipPath));
    }

    [Fact]
    public void Installer_UpgradesPreviouslyOwnedHookDefinition()
    {
        using var fixture = new TemporaryFixture();
        var command = new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, "legacy script");
        File.WriteAllText(
            fixture.Paths.OwnershipPath,
            new JsonObject
            {
                ["schema_version"] = 1,
                ["group_name"] = "codex-discord-presence",
                ["script_path"] = fixture.Paths.ScriptPath,
                ["script_sha256"] = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes("legacy script")))
            }.ToJsonString());
        File.WriteAllText(
            fixture.Paths.SettingsPath,
            new JsonObject
            {
                ["codex-discord-presence"] =
                    AntigravityHookCommandBuilder.CreateLegacyManagedDefinition(command.Commands!)
            }.ToJsonString());

        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityHookOperationStatus.Installed, installer.Install().Status);
        var installed = JsonNode.Parse(File.ReadAllText(fixture.Paths.SettingsPath))!.AsObject();
        var owned = Assert.IsType<JsonObject>(installed["codex-discord-presence"]);
        Assert.NotNull(owned["PreToolUse"]);
        Assert.Contains("'PreToolUse'", File.ReadAllText(fixture.Paths.ScriptPath), StringComparison.Ordinal);
        Assert.Equal(AntigravityHookOperationStatus.AlreadyInstalled, installer.Install().Status);
    }

    [Fact]
    public void Installer_ChangedOwnedScriptReturnsConflictAndPreservesIt()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityHookOperationStatus.Installed, installer.Install().Status);
        File.WriteAllText(fixture.Paths.ScriptPath, "user edit");

        var installResult = installer.Install();
        var uninstallResult = installer.Uninstall();

        Assert.Equal(AntigravityHookOperationStatus.Conflict, installResult.Status);
        Assert.Equal(AntigravityHookOperationStatus.Conflict, uninstallResult.Status);
        Assert.Equal("user edit", File.ReadAllText(fixture.Paths.ScriptPath));
        Assert.True(File.Exists(fixture.Paths.OwnershipPath));
    }

    private const string OfficialHookPayload = """
        {
          "conversationId": "conversation-id",
          "workspacePaths": ["C:\\repo"],
          "transcriptPath": "C:\\Users\\redacted\\.gemini\\antigravity-cli\\transcript.jsonl",
          "artifactDirectoryPath": "C:\\Users\\redacted\\.gemini\\antigravity-cli\\brain",
          "modelName": "Claude Sonnet 4.6 (Thinking)"
        }
        """;

    private const string OfficialToolHookPayload = """
        {
          "conversationId": "conversation-id",
          "workspacePaths": ["C:\\repo"],
          "transcriptPath": "C:\\Users\\redacted\\.gemini\\antigravity-cli\\transcript.jsonl",
          "artifactDirectoryPath": "C:\\Users\\redacted\\.gemini\\antigravity-cli\\brain",
          "modelName": "Claude Sonnet 4.6 (Thinking)",
          "toolCall": {
            "name": "read_file",
            "args": {
              "path": "C:\\repo\\README.md"
            }
          }
        }
        """;

    private static (int ExitCode, string Output, string Error) ExecutePowerShellScript(
        string scriptPath,
        string hookEvent,
        string payload)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        process.StartInfo.ArgumentList.Add("-NoLogo");
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-NonInteractive");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-HookEvent");
        process.StartInfo.ArgumentList.Add(hookEvent);

        Assert.True(process.Start());
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), error);
        return (process.ExitCode, output, error);
    }

    private sealed class TemporaryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "Antigravity Hook Tests",
            Guid.NewGuid().ToString("N"));

        internal TemporaryFixture()
        {
            Directory.CreateDirectory(_root);
            Paths = new(
                Path.Combine(_root, "user profile", ".gemini", "config", "hooks.json"),
                Path.Combine(_root, "app data", "antigravity-hook.ps1"),
                Path.Combine(_root, "app data", "events.jsonl"),
                Path.Combine(_root, "app data", "hooks-ownership.json"));
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.SettingsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.EventFilePath)!);
        }

        internal AntigravityHookPaths Paths { get; }

        internal AntigravityHookInstaller CreateInstaller() =>
            new(Paths, new AntigravityHookCommandBuilder(AntigravityStatusLinePlatform.Windows));

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_root))
                {
                    Directory.Delete(_root, recursive: true);
                }
            }
            catch (IOException)
            {
            }
        }
    }
}
