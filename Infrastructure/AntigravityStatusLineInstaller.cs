using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

public sealed record AntigravityStatusLinePaths(
    string SettingsPath,
    string ScriptPath,
    string EventFilePath,
    string BackupPath)
{
    public static AntigravityStatusLinePaths CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var integrationDirectory = Path.Combine(
            appData,
            "CodexDiscordPresence",
            "antigravity-status-line");

        return new(
            Path.Combine(userProfile, ".gemini", "antigravity-cli", "settings.json"),
            Path.Combine(integrationDirectory, "status-line.ps1"),
            Path.Combine(integrationDirectory, "events.jsonl"),
            Path.Combine(integrationDirectory, "settings-status-line-backup.json"));
    }
}

public enum AntigravityStatusLineOperationStatus
{
    Installed,
    AlreadyInstalled,
    Restored,
    NotInstalled,
    Conflict,
    InvalidSettings,
    Unsupported,
    Failed
}

public sealed record AntigravityStatusLineOperationResult(
    AntigravityStatusLineOperationStatus Status,
    string? Message = null)
{
    public bool Succeeded => Status is
        AntigravityStatusLineOperationStatus.Installed or
        AntigravityStatusLineOperationStatus.AlreadyInstalled or
        AntigravityStatusLineOperationStatus.Restored or
        AntigravityStatusLineOperationStatus.NotInstalled;
}

public sealed class AntigravityStatusLineInstaller
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };
    private readonly AntigravityStatusLineCommandBuildResult _command;
    private readonly AntigravityStatusLinePaths _paths;

    public AntigravityStatusLineInstaller()
        : this(AntigravityStatusLinePaths.CreateDefault(), new AntigravityStatusLineCommandBuilder())
    {
    }

    internal AntigravityStatusLineInstaller(
        AntigravityStatusLinePaths paths,
        IAntigravityStatusLineCommandBuilder commandBuilder)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentNullException.ThrowIfNull(commandBuilder);
        _command = commandBuilder.Build(_paths);
    }

    public AntigravityStatusLinePaths GetPaths() => _paths;

    public AntigravityStatusLineOperationResult Install()
    {
        if (!_command.IsSupported)
        {
            return Result(AntigravityStatusLineOperationStatus.Unsupported, _command.Error);
        }

        if (!TryLoadSettings(out var root, out var settingsError, out var settingsSnapshot))
        {
            return Result(AntigravityStatusLineOperationStatus.InvalidSettings, settingsError);
        }

        var propertyName = FindPropertyName(root, "statusLine");
        var existingValue = propertyName is null ? null : root[propertyName];
        var alreadyManaged = propertyName is not null && IsManagedStatusLine(existingValue);

        if (alreadyManaged)
        {
            if (!File.Exists(_paths.BackupPath))
            {
                return Result(
                    AntigravityStatusLineOperationStatus.Conflict,
                    "The application-managed statusLine has no backup to restore.");
            }

            if (!TryReadBackup(out _, out var backupError))
            {
                return Result(AntigravityStatusLineOperationStatus.Failed, backupError);
            }

            try
            {
                if (!File.Exists(_paths.ScriptPath))
                {
                    WriteAtomically(_paths.ScriptPath, _command.ScriptContent!, emitUtf8Bom: true);
                }

                return Result(AntigravityStatusLineOperationStatus.AlreadyInstalled);
            }
            catch (IOException ex)
            {
                return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
            }
        }

        if (propertyName is not null)
        {
            return Result(
                AntigravityStatusLineOperationStatus.Conflict,
                "A user-owned statusLine already exists and was left unchanged.");
        }

        if (File.Exists(_paths.BackupPath) || HasOwnedArtifactsWithoutBackup())
        {
            return Result(
                AntigravityStatusLineOperationStatus.Conflict,
                "Previous integration files exist but the current statusLine is not application-managed.");
        }

        try
        {
            var backup = new JsonObject
            {
                ["schema_version"] = 1,
                ["property_present"] = propertyName is not null,
                ["property_name"] = propertyName ?? "statusLine",
                ["value"] = existingValue?.DeepClone()
            };
            WriteAtomically(
                _paths.BackupPath,
                backup.ToJsonString(JsonOptions));
            var backupWritten = true;

            WriteAtomically(_paths.ScriptPath, _command.ScriptContent!, emitUtf8Bom: true);
            var scriptWritten = true;
            root[propertyName ?? "statusLine"] = new JsonObject
            {
                ["type"] = "command",
                ["command"] = _command.Command
            };

            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                DeleteIfExistsWhenCreated(_paths.ScriptPath, scriptWritten);
                DeleteIfExistsWhenCreated(_paths.BackupPath, backupWritten);
                return Result(
                    AntigravityStatusLineOperationStatus.Conflict,
                    "The Antigravity settings changed while the integration was being installed.");
            }

            WriteAtomically(_paths.SettingsPath, root.ToJsonString(JsonOptions));

            return Result(AntigravityStatusLineOperationStatus.Installed);
        }
        catch (IOException ex)
        {
            RollbackInstallArtifacts();
            return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            RollbackInstallArtifacts();
            return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
        }
    }

    public AntigravityStatusLineOperationResult Uninstall()
    {
        if (!_command.IsSupported)
        {
            return Result(AntigravityStatusLineOperationStatus.Unsupported, _command.Error);
        }

        if (!File.Exists(_paths.SettingsPath))
        {
            var hasOwnedArtifacts = HasOwnedArtifacts();
            return Result(
                hasOwnedArtifacts
                    ? AntigravityStatusLineOperationStatus.Conflict
                    : AntigravityStatusLineOperationStatus.NotInstalled,
                hasOwnedArtifacts
                    ? "The Antigravity settings file is missing; owned integration files were left unchanged."
                    : null);
        }

        if (!TryLoadSettings(out var root, out var settingsError, out var settingsSnapshot))
        {
            return Result(AntigravityStatusLineOperationStatus.InvalidSettings, settingsError);
        }

        var propertyName = FindPropertyName(root, "statusLine");
        if (propertyName is null || !IsManagedStatusLine(root[propertyName]))
        {
            var status = HasOwnedArtifacts()
                ? AntigravityStatusLineOperationStatus.Conflict
                : AntigravityStatusLineOperationStatus.NotInstalled;
            return Result(
                status,
                "The current statusLine is not managed by this application and was left unchanged.");
        }

        if (!File.Exists(_paths.BackupPath))
        {
            return Result(
                AntigravityStatusLineOperationStatus.Conflict,
                "The application-managed statusLine has no backup to restore.");
        }

        try
        {
            if (TryReadBackup(out var backup, out var backupError))
            {
                var originalPropertyName = backup.PropertyName ?? propertyName;
                if (backup.PropertyPresent)
                {
                    root[originalPropertyName] = backup.Value?.DeepClone();
                }
                else
                {
                    root.Remove(propertyName);
                }
            }
            else
            {
                return Result(AntigravityStatusLineOperationStatus.Failed, backupError);
            }

            if (backup.PropertyPresent &&
                !string.Equals(backup.PropertyName, propertyName, StringComparison.Ordinal))
            {
                root.Remove(propertyName);
            }

            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                return Result(
                    AntigravityStatusLineOperationStatus.Conflict,
                    "The Antigravity settings changed while the integration was being removed.");
            }

            WriteAtomically(_paths.SettingsPath, root.ToJsonString(JsonOptions));
            DeleteOwnedFiles();
            return Result(AntigravityStatusLineOperationStatus.Restored);
        }
        catch (IOException ex)
        {
            return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result(AntigravityStatusLineOperationStatus.Failed, ex.Message);
        }
    }

    private bool IsManagedStatusLine(JsonNode? value)
    {
        if (value is not JsonObject statusLine ||
            statusLine.Count != 2 ||
            !TryGetString(statusLine, "type", out var type) ||
            !string.Equals(type, "command", StringComparison.OrdinalIgnoreCase) ||
            !TryGetString(statusLine, "command", out var command))
        {
            return false;
        }

        return string.Equals(command, _command.Command, StringComparison.OrdinalIgnoreCase);
    }

    private bool HasOwnedArtifacts() =>
        File.Exists(_paths.BackupPath) ||
        File.Exists(_paths.ScriptPath) ||
        File.Exists(_paths.EventFilePath);

    private bool HasOwnedArtifactsWithoutBackup() =>
        File.Exists(_paths.ScriptPath) ||
        File.Exists(_paths.EventFilePath);

    private bool TryLoadSettings(
        out JsonObject root,
        out string error,
        out SettingsFileSnapshot snapshot)
    {
        root = new JsonObject();
        error = "";
        snapshot = new SettingsFileSnapshot(false, null);
        if (!File.Exists(_paths.SettingsPath))
        {
            return true;
        }

        try
        {
            var contents = File.ReadAllText(_paths.SettingsPath);
            snapshot = new SettingsFileSnapshot(true, contents);
            if (string.IsNullOrWhiteSpace(contents))
            {
                return true;
            }

            var parsed = JsonNode.Parse(
                contents,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });
            if (parsed is not JsonObject parsedObject)
            {
                error = "Antigravity settings JSON must contain an object.";
                return false;
            }

            root = parsedObject;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Antigravity settings JSON is invalid: {ex.Message}";
            return false;
        }
        catch (IOException ex)
        {
            error = ex.Message;
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    private bool TryReadBackup(out BackupDocument backup, out string error)
    {
        backup = new BackupDocument(false, null, null);
        error = "";
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_paths.BackupPath)) as JsonObject;
            if (root is null)
            {
                error = "The saved Antigravity statusLine backup is invalid.";
                return false;
            }

            var valuePropertyName = FindPropertyName(root, "value");
            var propertyName = FindPropertyName(root, "property_name");
            var schemaVersion = FindProperty(root, "schema_version");
            var propertyPresentValue = FindProperty(root, "property_present");
            if (schemaVersion is not JsonValue schemaVersionValue ||
                !schemaVersionValue.TryGetValue<int>(out var schemaVersionNumber) ||
                schemaVersionNumber != 1 ||
                propertyPresentValue is not JsonValue propertyPresentJson ||
                !propertyPresentJson.TryGetValue<bool>(out var propertyPresent) ||
                propertyName is null ||
                !TryGetString(root, "property_name", out var originalPropertyName) ||
                string.IsNullOrWhiteSpace(originalPropertyName) ||
                valuePropertyName is null)
            {
                error = "The saved Antigravity statusLine backup is invalid.";
                return false;
            }

            backup = new BackupDocument(
                propertyPresent,
                originalPropertyName,
                FindProperty(root, "value")?.DeepClone());
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException or IOException or UnauthorizedAccessException)
        {
            error = "The saved Antigravity statusLine backup is invalid.";
            return false;
        }
    }

    private void DeleteOwnedFiles()
    {
        DeleteIfExists(_paths.ScriptPath);
        DeleteIfExists(_paths.EventFilePath);
        DeleteIfExists(_paths.BackupPath);
    }

    private void RollbackInstallArtifacts()
    {
        DeleteIfExists(_paths.ScriptPath);
        DeleteIfExists(_paths.BackupPath);
    }

    private static void DeleteIfExistsWhenCreated(string path, bool wasCreated)
    {
        if (wasCreated)
        {
            DeleteIfExists(path);
        }
    }

    private bool MatchesSettingsSnapshot(SettingsFileSnapshot snapshot)
    {
        try
        {
            if (File.Exists(_paths.SettingsPath) != snapshot.Exists)
            {
                return false;
            }

            return !snapshot.Exists ||
                string.Equals(
                    File.ReadAllText(_paths.SettingsPath),
                    snapshot.Contents,
                    StringComparison.Ordinal);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void DeleteIfExists(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void WriteAtomically(
        string path,
        string contents,
        bool emitUtf8Bom = false)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, contents, new System.Text.UTF8Encoding(emitUtf8Bom));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static JsonNode? FindProperty(JsonObject node, string propertyName)
    {
        var name = FindPropertyName(node, propertyName);
        return name is null ? null : node[name];
    }

    private static string? FindPropertyName(JsonObject node, string propertyName) =>
        node.Select(pair => pair.Key)
            .FirstOrDefault(key => string.Equals(key, propertyName, StringComparison.OrdinalIgnoreCase));

    private static bool TryGetString(JsonObject node, string propertyName, out string value)
    {
        var property = FindProperty(node, propertyName);
        if (property is JsonValue jsonValue && jsonValue.TryGetValue<string>(out value!))
        {
            return true;
        }

        value = "";
        return false;
    }

    private static AntigravityStatusLineOperationResult Result(
        AntigravityStatusLineOperationStatus status,
        string? message = null) => new(status, message);

    private sealed record BackupDocument(
        bool PropertyPresent,
        string? PropertyName,
        JsonNode? Value);

    private sealed record SettingsFileSnapshot(bool Exists, string? Contents);
}
