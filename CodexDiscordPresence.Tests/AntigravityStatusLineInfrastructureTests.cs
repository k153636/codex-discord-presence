using System.Diagnostics;
using System.Text;
using System.Text.Json;
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
    public void Installer_InstallAndUninstall_PreservesUnknownSettingsAndRestoresUserStatusLine()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(
            fixture.SettingsPath,
            "{\"futureProperty\":{\"keep\":true},\"statusLine\":{\"type\":\"command\",\"command\":\"other-tool --status\"}}");
        var installer = fixture.CreateInstaller();

        var install = installer.Install();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, install.Status);
        using (var settings = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath)))
        {
            Assert.True(settings.RootElement.GetProperty("futureProperty").GetProperty("keep").GetBoolean());
            Assert.Contains(
                fixture.Paths.ScriptPath,
                settings.RootElement.GetProperty("statusLine").GetProperty("command").GetString(),
                StringComparison.Ordinal);
        }

        Assert.True(File.Exists(fixture.Paths.BackupPath));
        var uninstall = installer.Uninstall();
        Assert.Equal(AntigravityStatusLineOperationStatus.Restored, uninstall.Status);
        using var restored = JsonDocument.Parse(File.ReadAllText(fixture.SettingsPath));
        Assert.Equal(
            "other-tool --status",
            restored.RootElement.GetProperty("statusLine").GetProperty("command").GetString());
        Assert.False(File.Exists(fixture.Paths.ScriptPath));
        Assert.False(File.Exists(fixture.Paths.EventFilePath));
        Assert.False(File.Exists(fixture.Paths.BackupPath));
    }

    [Fact]
    public void Installer_SecondInstall_IsIdempotentAndKeepsOriginalBackup()
    {
        using var fixture = new TemporaryFixture();
        File.WriteAllText(fixture.SettingsPath, "{\"statusLine\":\"user-owned\"}");
        var installer = fixture.CreateInstaller();

        Assert.Equal(AntigravityStatusLineOperationStatus.Installed, installer.Install().Status);
        var backupBefore = File.ReadAllText(fixture.Paths.BackupPath);
        Assert.Equal(AntigravityStatusLineOperationStatus.AlreadyInstalled, installer.Install().Status);
        Assert.Equal(backupBefore, File.ReadAllText(fixture.Paths.BackupPath));
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
    public void CommandBuilder_Windows_QuotesPathsWithSpacesAndScriptKeepsContractSafe()
    {
        using var fixture = new TemporaryFixture();
        var result = new AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform.Windows)
            .Build(fixture.Paths);

        Assert.True(result.IsSupported);
        Assert.Contains($"-File \"{fixture.Paths.ScriptPath}\"", result.Command, StringComparison.Ordinal);
        Assert.Contains("$MaxPayloadBytes = 262144", result.ScriptContent, StringComparison.Ordinal);
        Assert.Contains("ConvertFrom-Json", result.ScriptContent, StringComparison.Ordinal);
        Assert.Contains("Write-BoundedEvent", result.ScriptContent, StringComparison.Ordinal);
        Assert.DoesNotContain("Write-Output", result.ScriptContent, StringComparison.Ordinal);
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

    private static ProviderObservation CreateObservation(string modelId) => new(
        ProviderObservationSource.AntigravityCli,
        DateTimeOffset.UtcNow,
        ProviderAgentState.Working,
        new ProviderModelObservation(modelId, "Test model"),
        new ProviderWorkspaceObservation(null, "workspace", "project"),
        "conversation");

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
          "email": "redacted@example.invalid"
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
