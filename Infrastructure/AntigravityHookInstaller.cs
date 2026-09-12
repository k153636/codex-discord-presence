using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

internal sealed record AntigravityHookPaths(
    string SettingsPath,
    string ScriptPath,
    string EventFilePath,
    string OwnershipPath)
{
    public static AntigravityHookPaths CreateDefault()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var integrationDirectory = Path.Combine(
            appData,
            "CodexDiscordPresence",
            "antigravity-status-line");

        return new(
            Path.Combine(userProfile, ".gemini", "config", "hooks.json"),
            Path.Combine(integrationDirectory, "antigravity-hook.ps1"),
            Path.Combine(integrationDirectory, "hook-events.jsonl"),
            Path.Combine(integrationDirectory, "hooks-ownership.json"));
    }
}

internal enum AntigravityHookOperationStatus
{
    Installed,
    AlreadyInstalled,
    NotInstalled,
    Conflict,
    InvalidSettings,
    Unsupported,
    Failed
}

internal sealed record AntigravityHookOperationResult(
    AntigravityHookOperationStatus Status,
    string? Message = null)
{
    internal bool Succeeded => Status is
        AntigravityHookOperationStatus.Installed or
        AntigravityHookOperationStatus.AlreadyInstalled or
        AntigravityHookOperationStatus.NotInstalled;
}

internal sealed class AntigravityHookInstaller
{
    private const string GroupName = "codex-discord-presence";
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly AntigravityHookCommandBuildResult _command;
    private readonly AntigravityHookPaths _paths;
    private readonly JsonObject? _managedDefinition;

    public AntigravityHookInstaller()
        : this(AntigravityHookPaths.CreateDefault(), new AntigravityHookCommandBuilder())
    {
    }

    internal AntigravityHookInstaller(
        AntigravityHookPaths paths,
        IAntigravityHookCommandBuilder commandBuilder)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        ArgumentNullException.ThrowIfNull(commandBuilder);
        _command = commandBuilder.Build(_paths);
        _managedDefinition = _command.Commands is null
            ? null
            : AntigravityHookCommandBuilder.CreateManagedDefinition(_command.Commands);
    }

    internal AntigravityHookPaths GetPaths() => _paths;

    internal AntigravityHookOperationResult Install()
    {
        if (!_command.IsSupported || _managedDefinition is null)
        {
            return Result(AntigravityHookOperationStatus.Unsupported, _command.Error);
        }

        if (!TryLoadSettings(out var root, out var settingsError, out var settingsSnapshot))
        {
            return Result(AntigravityHookOperationStatus.InvalidSettings, settingsError);
        }

        var existingDefinition = root[GroupName];
        if (existingDefinition is not null)
        {
            if (!TryReadOwnership(out var ownership, out var ownershipError))
            {
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    ownershipError ?? "A user-owned or modified Antigravity hook group already exists and was left unchanged.");
            }

            if (ownership is null || !MatchesOwnedScript(ownership))
            {
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    "The application-owned Antigravity hook script was changed and was left unchanged.");
            }

            if (JsonNode.DeepEquals(existingDefinition, _managedDefinition))
            {
                return Result(AntigravityHookOperationStatus.AlreadyInstalled);
            }

            if (JsonNode.DeepEquals(
                    existingDefinition,
                    AntigravityHookCommandBuilder.CreateLegacyManagedDefinition(_command.Commands!)))
            {
                return UpgradeOwnedInstallation(root, settingsSnapshot);
            }

            return Result(
                AntigravityHookOperationStatus.Conflict,
                "A user-owned or modified Antigravity hook group already exists and was left unchanged.");
        }

        if (File.Exists(_paths.OwnershipPath) ||
            File.Exists(_paths.ScriptPath) ||
            File.Exists(_paths.EventFilePath))
        {
            return Result(
                AntigravityHookOperationStatus.Conflict,
                "Previous Antigravity hook files exist without an owned hook group.");
        }

        var scriptWritten = false;
        var ownershipWritten = false;
        try
        {
            WriteAtomically(_paths.ScriptPath, _command.ScriptContent!, emitUtf8Bom: true);
            scriptWritten = true;

            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                RollbackArtifacts(scriptWritten, ownershipWritten);
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    "The Antigravity hooks settings changed while the integration was being installed.");
            }

            WriteAtomically(_paths.OwnershipPath, CreateOwnershipDocument().ToJsonString(JsonOptions));
            ownershipWritten = true;

            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                RollbackArtifacts(scriptWritten, ownershipWritten);
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    "The Antigravity hooks settings changed while the integration was being installed.");
            }

            root[GroupName] = _managedDefinition.DeepClone();
            WriteAtomically(_paths.SettingsPath, root.ToJsonString(JsonOptions));
            return Result(AntigravityHookOperationStatus.Installed);
        }
        catch (IOException ex)
        {
            RollbackArtifacts(scriptWritten, ownershipWritten);
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            RollbackArtifacts(scriptWritten, ownershipWritten);
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
    }

    internal AntigravityHookOperationResult Uninstall()
    {
        if (!_command.IsSupported || _managedDefinition is null)
        {
            return Result(AntigravityHookOperationStatus.Unsupported, _command.Error);
        }

        if (!File.Exists(_paths.SettingsPath))
        {
            return Result(
                HasOwnedArtifacts()
                    ? AntigravityHookOperationStatus.Conflict
                    : AntigravityHookOperationStatus.NotInstalled,
                HasOwnedArtifacts()
                    ? "The Antigravity hooks settings file is missing; owned files were left unchanged."
                    : null);
        }

        if (!TryLoadSettings(out var root, out var settingsError, out var settingsSnapshot))
        {
            return Result(AntigravityHookOperationStatus.InvalidSettings, settingsError);
        }

        var existingDefinition = root[GroupName];
        if (existingDefinition is null)
        {
            return Result(
                HasOwnedArtifacts()
                    ? AntigravityHookOperationStatus.Conflict
                    : AntigravityHookOperationStatus.NotInstalled,
                HasOwnedArtifacts()
                    ? "The Antigravity hook group is missing; owned files were left unchanged."
                    : null);
        }

        HookOwnershipDocument? ownership = null;
        string? ownershipError = null;
        if (!IsKnownManagedDefinition(existingDefinition) ||
            !TryReadOwnership(out ownership, out ownershipError))
        {
            return Result(
                AntigravityHookOperationStatus.Conflict,
                ownershipError ?? "The Antigravity hook group was changed and was left unchanged.");
        }

        if (ownership is null || !MatchesOwnedScript(ownership))
        {
            return Result(
                AntigravityHookOperationStatus.Conflict,
                "The application-owned Antigravity hook script was changed and was left unchanged.");
        }

        try
        {
            root.Remove(GroupName);
            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    "The Antigravity hooks settings changed while the integration was being removed.");
            }

            WriteAtomically(_paths.SettingsPath, root.ToJsonString(JsonOptions));
            DeleteIfExists(_paths.ScriptPath);
            DeleteIfExists(_paths.OwnershipPath);
            DeleteIfExists(_paths.EventFilePath);
            return Result(AntigravityHookOperationStatus.NotInstalled);
        }
        catch (IOException ex)
        {
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
    }

    private AntigravityHookOperationResult UpgradeOwnedInstallation(
        JsonObject root,
        SettingsFileSnapshot settingsSnapshot)
    {
        var originalScript = "";
        var originalOwnership = "";
        var scriptUpdated = false;
        var ownershipUpdated = false;

        try
        {
            originalScript = File.ReadAllText(_paths.ScriptPath);
            originalOwnership = File.ReadAllText(_paths.OwnershipPath);

            WriteAtomically(_paths.ScriptPath, _command.ScriptContent!, emitUtf8Bom: true);
            scriptUpdated = true;
            WriteAtomically(
                _paths.OwnershipPath,
                CreateOwnershipDocument().ToJsonString(JsonOptions));
            ownershipUpdated = true;

            if (!MatchesSettingsSnapshot(settingsSnapshot))
            {
                RestoreOwnedInstallation(originalScript, originalOwnership, scriptUpdated, ownershipUpdated);
                return Result(
                    AntigravityHookOperationStatus.Conflict,
                    "The Antigravity hooks settings changed while the integration was being upgraded.");
            }

            root[GroupName] = _managedDefinition!.DeepClone();
            WriteAtomically(_paths.SettingsPath, root.ToJsonString(JsonOptions));
            return Result(AntigravityHookOperationStatus.Installed);
        }
        catch (IOException ex)
        {
            RestoreOwnedInstallation(originalScript, originalOwnership, scriptUpdated, ownershipUpdated);
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            RestoreOwnedInstallation(originalScript, originalOwnership, scriptUpdated, ownershipUpdated);
            return Result(AntigravityHookOperationStatus.Failed, ex.Message);
        }
    }

    private void RestoreOwnedInstallation(
        string originalScript,
        string originalOwnership,
        bool scriptUpdated,
        bool ownershipUpdated)
    {
        if (ownershipUpdated)
        {
            WriteAtomically(_paths.OwnershipPath, originalOwnership);
        }

        if (scriptUpdated)
        {
            WriteAtomically(_paths.ScriptPath, originalScript, emitUtf8Bom: true);
        }
    }

    private bool IsKnownManagedDefinition(JsonNode existingDefinition)
    {
        return JsonNode.DeepEquals(existingDefinition, _managedDefinition) ||
            JsonNode.DeepEquals(
                existingDefinition,
                AntigravityHookCommandBuilder.CreateLegacyManagedDefinition(_command.Commands!));
    }

    private JsonObject CreateOwnershipDocument() => new()
    {
        ["schema_version"] = 1,
        ["group_name"] = GroupName,
        ["script_path"] = _paths.ScriptPath,
        ["script_sha256"] = ComputeScriptHash()
    };

    private bool TryReadOwnership(
        out HookOwnershipDocument? ownership,
        out string? error)
    {
        ownership = null;
        error = null;
        if (!File.Exists(_paths.OwnershipPath))
        {
            error = "The Antigravity hook ownership marker is missing.";
            return false;
        }

        try
        {
            var root = JsonNode.Parse(File.ReadAllText(_paths.OwnershipPath)) as JsonObject;
            if (root is null ||
                !TryReadInt(root, "schema_version", out var schemaVersion) ||
                schemaVersion != 1 ||
                !TryReadString(root, "group_name", out var groupName) ||
                !string.Equals(groupName, GroupName, StringComparison.Ordinal) ||
                !TryReadString(root, "script_path", out var scriptPath) ||
                !string.Equals(scriptPath, _paths.ScriptPath, StringComparison.Ordinal) ||
                !TryReadString(root, "script_sha256", out var scriptHash) ||
                scriptHash.Length != 64)
            {
                error = "The Antigravity hook ownership marker is invalid.";
                return false;
            }

            ownership = new HookOwnershipDocument(scriptPath, scriptHash);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            error = "The Antigravity hook ownership marker is invalid.";
            return false;
        }
    }

    private bool MatchesOwnedScript(HookOwnershipDocument ownership)
    {
        try
        {
            return File.Exists(_paths.ScriptPath) &&
                string.Equals(ComputeScriptHash(), ownership.ScriptSha256, StringComparison.OrdinalIgnoreCase);
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
                error = "Antigravity hooks JSON must contain an object.";
                return false;
            }

            root = parsedObject;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"Antigravity hooks JSON is invalid: {ex.Message}";
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

    private bool MatchesSettingsSnapshot(SettingsFileSnapshot snapshot)
    {
        try
        {
            return File.Exists(_paths.SettingsPath) == snapshot.Exists &&
                (!snapshot.Exists || string.Equals(
                    File.ReadAllText(_paths.SettingsPath),
                    snapshot.Contents,
                    StringComparison.Ordinal));
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

    private void RollbackArtifacts(bool scriptWritten, bool ownershipWritten)
    {
        if (ownershipWritten)
        {
            DeleteIfExists(_paths.OwnershipPath);
        }

        if (scriptWritten)
        {
            DeleteIfExists(_paths.ScriptPath);
        }
    }

    private bool HasOwnedArtifacts() =>
        File.Exists(_paths.OwnershipPath) ||
        File.Exists(_paths.ScriptPath) ||
        File.Exists(_paths.EventFilePath);

    private string ComputeScriptHash() =>
        Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(_paths.ScriptPath)));

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
            File.WriteAllText(
                temporaryPath,
                contents,
                emitUtf8Bom
                    ? new UTF8Encoding(encoderShouldEmitUTF8Identifier: true)
                    : new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            DeleteIfExists(temporaryPath);
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

    private static bool TryReadInt(JsonObject root, string propertyName, out int value)
    {
        value = 0;
        return root[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue(out value);
    }

    private static bool TryReadString(JsonObject root, string propertyName, out string value)
    {
        value = "";
        return root[propertyName] is JsonValue jsonValue &&
            jsonValue.TryGetValue<string>(out value!);
    }

    private static AntigravityHookOperationResult Result(
        AntigravityHookOperationStatus status,
        string? message = null) => new(status, message);

    private sealed record HookOwnershipDocument(string ScriptPath, string ScriptSha256);

    private sealed record SettingsFileSnapshot(bool Exists, string? Contents);
}
