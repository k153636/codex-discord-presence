using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class AntigravityStatusLineInfrastructureTests
{
    [Fact]
    public void EventStore_MissingFile_ReturnsFalseWithoutThrowing()
    {
        using var fixture = new TemporaryFixture();
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);

        Assert.False(store.TryReadLatest(out var observation));
        Assert.Null(observation);
    }

    [Fact]
    public void EventStore_OfficialPayload_WritesSafeBoundedContract()
    {
        using var fixture = new TemporaryFixture();
        const string projectPath = @"C:\Users\Souma\Projects\Antigravity Demo";
        var parser = new AntigravityStatusLinePayloadParser();
        Assert.True(parser.TryParse(
            Encoding.UTF8.GetBytes(OfficialPayload),
            DateTimeOffset.UtcNow,
            out var observation));

        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        Assert.True(store.TryAppend(observation!, projectPath));
        Assert.True(store.TryReadLatest(projectPath, out var latest));

        Assert.Equal("my-project", latest?.Workspace?.WorkspaceName);
        Assert.Equal("gemini-3.5-flash-high", latest?.Model?.Id);
        Assert.Equal(ProviderExecutionMode.Planning, latest?.ExecutionMode);
        Assert.Equal(149318, latest?.ContextWindow?.TotalTokens);
        var persisted = File.ReadAllText(fixture.EventFilePath);
        Assert.DoesNotContain("redacted@example.invalid", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript.jsonl", persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(projectPath, persisted, StringComparison.Ordinal);
        Assert.True(new FileInfo(fixture.EventFilePath).Length <= AntigravityStatusLineEventStore.MaxEventFileBytes);
    }

    [Fact]
    public void EventStore_ReadLatest_SkipsMalformedAndPartialLines()
    {
        using var fixture = new TemporaryFixture();
        var first = CreateObservation("first");
        var second = CreateObservation("second");
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);

        Assert.True(store.TryAppend(first));
        File.AppendAllText(fixture.EventFilePath, "{not-json}\n{\"schema_version\":1");
        Assert.True(store.TryAppend(second));

        Assert.True(store.TryReadLatest(out var latest));
        Assert.Equal("second", latest?.Model?.Id);
    }

    [Fact]
    public void EventStore_ReadLatest_HandlesOversizedFileByReadingBoundedTail()
    {
        using var fixture = new TemporaryFixture();
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        var validStore = new AntigravityStatusLineEventStore(fixture.ValidEventFilePath);
        Assert.True(validStore.TryAppend(CreateObservation("tail")));
        var validLine = File.ReadAllText(fixture.ValidEventFilePath);
        File.WriteAllText(fixture.EventFilePath, new string('x', AntigravityStatusLineEventStore.MaxEventFileBytes) + "\n" + validLine);

        Assert.True(store.TryReadLatest(out var latest));
        Assert.Equal("tail", latest?.Model?.Id);
    }

    [Fact]
    public void EventStore_Append_TrimsCountAndFileSize()
    {
        using var fixture = new TemporaryFixture();
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);

        for (var index = 0; index < AntigravityStatusLineEventStore.MaxEventCount + 5; index++)
        {
            Assert.True(store.TryAppend(CreateObservation(index.ToString())));
        }

        var lines = File.ReadAllLines(fixture.EventFilePath);
        Assert.Equal(AntigravityStatusLineEventStore.MaxEventCount, lines.Length);
        Assert.True(new FileInfo(fixture.EventFilePath).Length <= AntigravityStatusLineEventStore.MaxEventFileBytes);
        Assert.True(store.TryReadLatest(out var latest));
        Assert.Equal((AntigravityStatusLineEventStore.MaxEventCount + 4).ToString(), latest?.Model?.Id);
    }

    [Fact]
    public void EventStore_ProjectFilter_UsesOpaqueKeyWithoutPersistingPath()
    {
        using var fixture = new TemporaryFixture();
        const string firstProject = @"C:\Program Files\Antigravity\one";
        const string secondProject = @"C:\Program Files\Antigravity\two";
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);

        Assert.True(store.TryAppend(CreateObservation("first"), firstProject));
        Assert.True(store.TryAppend(CreateObservation("second"), secondProject));
        Assert.True(store.TryReadLatest(firstProject, out var latest));

        Assert.Equal("first", latest?.Model?.Id);
        var persisted = File.ReadAllText(fixture.EventFilePath);
        Assert.DoesNotContain(firstProject, persisted, StringComparison.Ordinal);
        Assert.DoesNotContain(secondProject, persisted, StringComparison.Ordinal);
        Assert.Matches("[0-9A-F]{64}", persisted);
    }

    [Fact]
    public void EventStore_ReadLatestByConversation_KeepsOnlyNewestObservationPerConversation()
    {
        using var fixture = new TemporaryFixture();
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        var firstConversationOld = CreateObservation(
            "conversation-a",
            DateTimeOffset.Parse("2026-09-12T01:00:00Z"));
        var firstConversationNew = CreateObservation(
            "conversation-a",
            DateTimeOffset.Parse("2026-09-12T01:01:00Z"));
        var secondConversation = CreateObservation(
            "conversation-b",
            DateTimeOffset.Parse("2026-09-12T01:02:00Z"));

        Assert.True(store.TryAppend(firstConversationOld));
        Assert.True(store.TryAppend(firstConversationNew));
        Assert.True(store.TryAppend(secondConversation));

        Assert.True(store.TryReadLatestByConversation(null, out var observations));
        Assert.Equal(2, observations.Count);
        Assert.Equal(
            ["conversation-b", "conversation-a"],
            observations.Select(observation => observation.ConversationId!).ToArray());
        Assert.Equal(
            firstConversationNew.ObservedAtUtc,
            observations.Single(observation => observation.ConversationId == "conversation-a").ObservedAtUtc);
    }

    [Fact]
    public void Installer_ExistingDisabledStatusLine_ReturnsConflictWithoutWritingFiles()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.SettingsPath,
            "{\"futureProperty\":{\"keep\":true},\"StatusLine\":{\"type\":\"command\",\"command\":\"other-tool --status\",\"enabled\":false}}");
        var installer = fixture.CreateInstaller();

        var install = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Conflict, install.Status);
        Assert.Contains("left unchanged", install.Message, StringComparison.OrdinalIgnoreCase);
        using var settings = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.True(settings.RootElement.GetProperty("futureProperty").GetProperty("keep").GetBoolean());
        Assert.Equal(
            "other-tool --status",
            settings.RootElement.GetProperty("StatusLine").GetProperty("command").GetString());
        Assert.False(File.Exists(fixture.Paths.BackupPath));
        Assert.False(File.Exists(fixture.Paths.ScriptPath));
        Assert.False(File.Exists(fixture.Paths.EventFilePath));
    }

    [Fact]
    public void Installer_SecondInstall_IsIdempotentAndKeepsOriginalBackup()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        var backupBefore = File.ReadAllText(fixture.Paths.BackupPath);
        var settingsBefore = File.ReadAllText(fixture.SettingsPath);
        Assert.Equal(AntigravityStatusLineOperationStatus.AlreadyInstalled, installer.Install().Status);
        Assert.Equal(backupBefore, File.ReadAllText(fixture.Paths.BackupPath));
        Assert.Equal(settingsBefore, File.ReadAllText(fixture.SettingsPath));
    }

    [Fact]
    public void Installer_ExistingManagedIntegration_RefreshesManagedScript()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        File.WriteAllText(fixture.Paths.ScriptPath, "outdated script");

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.AlreadyInstalled, result.Status);
        Assert.Contains("Move-Item -LiteralPath", File.ReadAllText(fixture.Paths.ScriptPath), StringComparison.Ordinal);
    }

    [Fact]
    public void Installer_ActiveUserStatusLine_ReturnsConflictWithoutWritingFiles()
    {
        using var fixture = new TemporaryFixture();
        const string settings = "{\"futureProperty\":{\"keep\":true},\"statusLine\":{\"type\":\"command\",\"command\":\"other-tool --status\"}}";
        File.WriteAllText(fixture.SettingsPath, settings);
        var installer = fixture.CreateInstaller();

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Conflict, result.Status);
        Assert.Equal(settings, File.ReadAllText(fixture.SettingsPath));
        Assert.False(File.Exists(fixture.Paths.BackupPath));
        Assert.False(File.Exists(fixture.Paths.ScriptPath));
        Assert.False(File.Exists(fixture.Paths.EventFilePath));
    }

    [Fact]
    public void Installer_UserChangesManagedStatusLine_UninstallReturnsConflictWithoutChangingIt()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);

        const string changedSettings = "{\"statusLine\":{\"type\":\"command\",\"command\":\"user-command\"}}";
        File.WriteAllText(fixture.SettingsPath, changedSettings);

        var result = installer.Uninstall();

        Assert.Equal(AntigravityStatusLineOperationStatus.Conflict, result.Status);
        Assert.Equal(changedSettings, File.ReadAllText(fixture.SettingsPath));
        Assert.True(File.Exists(fixture.Paths.BackupPath));
        Assert.True(File.Exists(fixture.Paths.ScriptPath));
    }

    [Fact]
    public void Installer_UserAddsManagedStatusLineProperty_UninstallReturnsConflictWithoutChangingIt()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);

        var settings = JsonNode.Parse(File.ReadAllText(fixture.SettingsPath))!.AsObject();
        settings["statusLine"]!.AsObject()["enabled"] = false;
        var changedSettings = settings.ToJsonString();
        File.WriteAllText(fixture.SettingsPath, changedSettings);

        var result = installer.Uninstall();

        Assert.Equal(AntigravityStatusLineOperationStatus.Conflict, result.Status);
        Assert.Equal(changedSettings, File.ReadAllText(fixture.SettingsPath));
        Assert.True(File.Exists(fixture.Paths.BackupPath));
    }

    [Fact]
    public void Installer_MissingSettingsWithOwnedFiles_ReturnsConflictWithoutDeletingFiles()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        File.Delete(fixture.SettingsPath);

        var result = installer.Uninstall();

        Assert.Equal(AntigravityStatusLineOperationStatus.Conflict, result.Status);
        Assert.True(File.Exists(fixture.Paths.BackupPath));
        Assert.True(File.Exists(fixture.Paths.ScriptPath));
    }

    [Fact]
    public void Installer_MalformedSettings_DoesNotOverwriteSettings()
    {
        using var fixture = new TemporaryFixture();
        const string malformed = "{\"futureProperty\":";
        File.WriteAllText(fixture.SettingsPath, malformed);
        var installer = fixture.CreateInstaller();

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.InvalidSettings, result.Status);
        Assert.Equal(malformed, File.ReadAllText(fixture.SettingsPath));
        Assert.False(File.Exists(fixture.Paths.BackupPath));
    }

    [Fact]
    public void Installer_MissingSettings_CreatesSettingsAndRestoresMissingProperty()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        Assert.True(File.Exists(fixture.SettingsPath));
        Assert.Equal(AntigravityStatusLineOperationStatus.Restored, installer.Uninstall().Status);
        using var settings = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.False(settings.RootElement.TryGetProperty("statusLine", out _));
    }

    [Fact]
    public void Installer_EmptySettings_PreservesEmptyRootAfterUninstall()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(fixture.SettingsPath, "{}");
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        Assert.Equal(AntigravityStatusLineOperationStatus.Restored, installer.Uninstall().Status);

        using var settings = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Empty(settings.RootElement.EnumerateObject());
    }

    [Fact]
    public void Installer_LegacyQuotedCommand_MigratesToEncodedCommand()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);

        var settings = JsonNode.Parse(File.ReadAllText(fixture.SettingsPath))!.AsObject();
        settings["statusLine"]!["command"] =
            AntigravityStatusLineCommandBuilder.BuildLegacyQuotedCommand(fixture.Paths.ScriptPath);
        File.WriteAllText(fixture.SettingsPath, settings.ToJsonString());

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, result.Status);
        var migrated = JsonNode.Parse(File.ReadAllText(fixture.SettingsPath))!.AsObject();
        Assert.Equal(
            fixture.Paths.ScriptPath,
            DecodeScriptInvocation(migrated["statusLine"]!["command"]!.GetValue<string>()));
    }

    [Fact]
    public void Installer_ZeroByteSettings_InitializesSettingsAndInstallsIntegration()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(fixture.SettingsPath, "");
        var installer = fixture.CreateInstaller();

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, result.Status);
        using var settings = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Equal(
            "command",
            settings.RootElement.GetProperty("statusLine").GetProperty("type").GetString());
        Assert.True(File.Exists(fixture.Paths.BackupPath));
        Assert.True(File.Exists(fixture.Paths.ScriptPath));
    }

    [Fact]
    public void Installer_MalformedBackup_DoesNotChangeManagedSettings()
    {
        using var fixture = new TemporaryFixture();
        var installer = fixture.CreateInstaller();
        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        var managedSettings = File.ReadAllText(fixture.SettingsPath);
        File.WriteAllText(fixture.Paths.BackupPath, "{\"schema_version\":1}");

        var result = installer.Uninstall();

        Assert.Equal(AntigravityStatusLineOperationStatus.Failed, result.Status);
        Assert.Equal(managedSettings, File.ReadAllText(fixture.SettingsPath));
        Assert.True(File.Exists(fixture.Paths.BackupPath));
    }

    [Fact]
    public void Installer_ReadOnlyScriptTarget_ReturnsFailedWithoutThrowing()
    {
        using var fixture = new TemporaryFixture();
        Directory.CreateDirectory(fixture.Paths.ScriptPath);
        var installer = fixture.CreateInstaller();

        var result = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Failed, result.Status);
        Assert.False(File.Exists(fixture.SettingsPath));
        Assert.False(File.Exists(fixture.Paths.BackupPath));
    }

    [Fact]
    public void CommandBuilder_Windows_UsesEncodedCommandForPathsWithSpaces()
    {
        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);

        Assert.True(result.IsSupported);
        Assert.Contains("-EncodedCommand ", result.Command, StringComparison.Ordinal);
        Assert.DoesNotContain("-File", result.Command, StringComparison.Ordinal);
        Assert.Equal(fixture.Paths.ScriptPath, DecodeScriptInvocation(result.Command!));
        Assert.Contains("$MaxPayloadBytes = 262144", result.ScriptContent, StringComparison.Ordinal);
        Assert.Contains("ConvertFrom-Json", result.ScriptContent, StringComparison.Ordinal);
        Assert.Contains("Write-BoundedEvent", result.ScriptContent, StringComparison.Ordinal);
        Assert.Contains("Exit-WithStatus $statusLine", result.ScriptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("transcript", result.ScriptContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("task_count", result.ScriptContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("session_id", result.ScriptContent, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Write-Output", result.ScriptContent, StringComparison.Ordinal);
    }

    private static string DecodeScriptInvocation(string command)
    {
        var encodedCommand = command[(command.LastIndexOf(' ') + 1)..];
        var invocation = Encoding.Unicode.GetString(Convert.FromBase64String(encodedCommand));
        return invocation[3..^1].Replace("''", "'", StringComparison.Ordinal);
    }

    [Fact]
    public void CommandBuilder_WindowsScript_PersistsEventAndReturnsSafeStatusLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent);

        var execution = ExecutePowerShellScript(fixture.Paths.ScriptPath, WindowsSubdirectoryPayload);
        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("Working", execution.Output);

        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        Assert.True(store.TryReadLatest(@"C:\repo", out var observation));
        Assert.Equal(ProviderAgentState.Working, observation?.AgentState);
    }

    [Fact]
    public void CommandBuilder_WindowsScript_UpdatesExistingEventFile()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent);

        var firstExecution = ExecutePowerShellScript(fixture.Paths.ScriptPath, WindowsSubdirectoryPayload);
        Assert.Equal(0, firstExecution.ExitCode);
        Assert.Equal("Working", firstExecution.Output);

        var secondExecution = ExecutePowerShellScript(
            fixture.Paths.ScriptPath,
            WindowsSubdirectoryPayload.Replace("working", "thinking", StringComparison.Ordinal));
        Assert.Equal(0, secondExecution.ExitCode);
        Assert.Equal("Thinking", secondExecution.Output);

        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        Assert.True(store.TryReadLatest(@"C:\repo", out var observation));
        Assert.Equal(ProviderAgentState.Thinking, observation?.AgentState);
    }

    [Fact]
    public void CommandBuilder_WindowsEncodedCommand_ExecutesScriptWithSpaces()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent);

        var command = result.Command ?? throw new InvalidOperationException("The Windows command was not created.");
        var encodedCommand = command[(command.LastIndexOf(' ') + 1)..];
        var execution = ExecutePowerShellEncodedCommand(encodedCommand, WindowsSubdirectoryPayload);

        Assert.True(execution.ExitCode == 0, execution.Error);
        Assert.Equal("Working", execution.Output);
        var store = new AntigravityStatusLineEventStore(fixture.EventFilePath);
        Assert.True(store.TryReadLatest(@"C:\repo", out var observation));
        Assert.Equal(ProviderAgentState.Working, observation?.AgentState);
    }

    [Fact]
    public void CommandBuilder_WindowsScript_InvalidPayloadStillReturnsSafeStatusLine()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);
        File.WriteAllText(fixture.Paths.ScriptPath, result.ScriptContent);

        var execution = ExecutePowerShellScript(fixture.Paths.ScriptPath, "{not-json");

        Assert.Equal(0, execution.ExitCode);
        Assert.Equal("Idling", execution.Output);
        Assert.False(File.Exists(fixture.EventFilePath));
    }

    [Fact]
    public void CommandBuilder_UnsupportedPlatform_ReturnsUnsupportedWithoutCommand()
    {
        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Unsupported)
            .Build(fixture.Paths);

        Assert.False(result.IsSupported);
        Assert.Null(result.Command);
        Assert.Null(result.ScriptContent);
        Assert.Contains("Windows only", result.Error, StringComparison.Ordinal);
    }

    private static ProviderObservation CreateObservation(
        string modelId,
        DateTimeOffset? observedAt = null) => new(
        ProviderObservationSource.AntigravityCli,
        observedAt ?? DateTimeOffset.UtcNow,
        ProviderAgentState.Working,
        new ProviderModelObservation(modelId, "Test model"),
        new ProviderWorkspaceObservation(null, "workspace", "project"),
        modelId);

    private static (int ExitCode, string Output, string Error) ExecutePowerShellScript(
        string scriptPath,
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

        Assert.True(process.Start());
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), error);
        return (process.ExitCode, output, error);
    }

    private static (int ExitCode, string Output, string Error) ExecutePowerShellEncodedCommand(
        string encodedCommand,
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
        process.StartInfo.ArgumentList.Add("-EncodedCommand");
        process.StartInfo.ArgumentList.Add(encodedCommand);

        Assert.True(process.Start());
        process.StandardInput.Write(payload);
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), error);
        return (process.ExitCode, output, error);
    }

    private const string OfficialPayload = """
        {
          "cwd": "/home/redacted/my-project",
          "session_id": "legacy-session-id",
          "conversation_id": "conversation-id",
          "transcript_path": "/home/redacted/.gemini/transcript.jsonl",
          "model": {
            "id": "gemini-3.5-flash-high",
            "display_name": "Gemini 3.5 Flash (High)"
          },
          "workspace": {
            "current_dir": "/home/redacted/my-project",
            "project_dir": "/home/redacted/my-project"
          },
          "agent_state": "working",
          "execution_mode": "planning",
          "context_window": {
            "total_input_tokens": 88244,
            "total_output_tokens": 61074
          },
          "email": "redacted@example.invalid"
        }
        """;

    private const string WindowsSubdirectoryPayload = """
        {
          "cwd": "C:\\repo\\src",
          "conversation_id": "conversation-id",
          "model": {
            "id": "gemini-3.5-flash-high",
            "display_name": "Gemini 3.5 Flash (High)"
          },
          "workspace": {
            "current_dir": "C:\\repo\\src",
            "project_dir": "C:\\repo"
          },
          "agent_state": "working"
        }
        """;

    private sealed class TemporaryFixture : IDisposable
    {
        private readonly string _root = Path.Combine(
            Path.GetTempPath(),
            "Antigravity Status Line Tests",
            Guid.NewGuid().ToString("N"));

        public TemporaryFixture()
        {
            Directory.CreateDirectory(_root);
            Paths = new(
                Path.Combine(_root, "user profile", ".gemini", "antigravity-cli", "settings.json"),
                Path.Combine(_root, "app data", "status line.ps1"),
                Path.Combine(_root, "app data", "events.jsonl"),
                Path.Combine(_root, "app data", "backup.json"));
            EventFilePath = Paths.EventFilePath;
            ValidEventFilePath = Path.Combine(_root, "valid.jsonl");
            SettingsPath = Paths.SettingsPath;
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.SettingsPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Paths.EventFilePath)!);
        }

        public AntigravityStatusLinePaths Paths { get; }
        public string EventFilePath { get; }
        public string ValidEventFilePath { get; }
        public string SettingsPath { get; }

        public AntigravityStatusLineInstaller CreateInstaller() =>
            new(Paths, new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows));

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
