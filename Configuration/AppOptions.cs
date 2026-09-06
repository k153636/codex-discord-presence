using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

public sealed class AppOptions
{
    public DiscordOptions Discord { get; set; } = new();
    public DiscordOptions? DiscordCli { get; set; }
    public CodexDetectionOptions Codex { get; set; } = new();
    public CodexDetectionOptions? CodexCli { get; set; }
    public ProjectOptions Project { get; set; } = new();
    public PresenceTemplateOptions Presence { get; set; } = new();
    public TokenUsageOptions TokenUsage { get; set; } = new();
    public int UpdateIntervalSeconds { get; set; } = 2;
    public bool EnableUpdateCheck { get; set; } = true;

    public static AppOptions Load(string[] args)
    {
        return Load(args, AppPaths.Create(AppProfileKind.Codex));
    }

    public static AppOptions Load(string[] args, AppPaths paths)
    {
        var cliSettingsPath = Path.Combine(paths.BaseDirectory, SettingsFileNames.Cli);
        var options = LoadMerged(paths.ExecutableSettingsPath, cliSettingsPath, paths.UserSettingsPath);

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--client-id" when i + 1 < args.Length:
                    options.Discord.ClientId = args[++i];
                    break;
                case "--project" when i + 1 < args.Length:
                    options.Project.Path = args[++i];
                    break;
                case "--interval" when i + 1 < args.Length && int.TryParse(args[++i], out var interval):
                    options.UpdateIntervalSeconds = interval;
                    break;
                case "--model" when i + 1 < args.Length:
                    options.Presence.ModelName = args[++i];
                    break;
            }
        }

        return options;
    }

    public static AppOptions LoadFromFile(string path)
    {
        return File.Exists(path)
            ? JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path), JsonOptions()) ?? new AppOptions()
            : new AppOptions();
    }

    public static AppOptions LoadMerged(params string[] paths)
    {
        var merged = new JsonObject();

        foreach (var path in paths)
        {
            if (!TryLoadJsonObject(path, out var node))
            {
                continue;
            }

            MergeJsonObject(merged, node);
        }

        return merged.Deserialize<AppOptions>(JsonOptions()) ?? new AppOptions();
    }

    public CodexDetectionOptions GetCodexDetectionOptions(AppProfileKind profile)
    {
        return profile == AppProfileKind.CodexCli && CodexCli is not null
            ? CodexCli
            : Codex;
    }

    public DiscordOptions GetDiscordOptions(AppProfileKind profile)
    {
        return profile == AppProfileKind.CodexCli && DiscordCli is not null
            ? DiscordCli
            : Discord;
    }

    private static bool TryLoadJsonObject(string path, out JsonObject node)
    {
        node = new JsonObject();

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var parsed = JsonNode.Parse(
                File.ReadAllText(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                });
            if (parsed is JsonObject objectNode)
            {
                node = objectNode;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to read settings file '{path}': {ex.Message}");
            return false;
        }
    }

    private static void MergeJsonObject(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceObject)
            {
                if (target[key] is JsonObject targetObject)
                {
                    MergeJsonObject(targetObject, sourceObject);
                }
                else
                {
                    target[key] = sourceObject.DeepClone();
                }

                continue;
            }

            target[key] = value?.DeepClone();
        }
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };
}

public sealed class DiscordOptions
{
    public string ClientId { get; set; } = "1516846793873424474";
    public string? LargeImageKey { get; set; } = "rpc_thinking";
    public string? SmallImageKey { get; set; } = "rpc_codex";
    public Dictionary<string, string> ActivityImageKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(CodexActivityKind.Offline)] = "rpc_sleeping",
        [nameof(CodexActivityKind.Ready)] = "rpc_sleeping",
        [nameof(CodexActivityKind.AnalyzingProject)] = "rpc_thinking",
        [nameof(CodexActivityKind.ApplyingEdits)] = "rpc_coding",
        [nameof(CodexActivityKind.CoordinatingChanges)] = "rpc_coding",
        [nameof(CodexActivityKind.CreatingFiles)] = "rpc_coding",
        [nameof(CodexActivityKind.DeletingFiles)] = "rpc_coding",
        [nameof(CodexActivityKind.RunningCommand)] = "rpc_building",
        [nameof(CodexActivityKind.Planning)] = "rpc_thinking",
        [nameof(CodexActivityKind.Refactoring)] = "rpc_coding"
    };
    public Dictionary<string, string> RunningCommandImageKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        [nameof(RunningCommandKind.Unknown)] = "rpc_building",
        [nameof(RunningCommandKind.Git)] = "rpc_reading",
        [nameof(RunningCommandKind.Search)] = "rpc_searching",
        [nameof(RunningCommandKind.Build)] = "rpc_building",
        [nameof(RunningCommandKind.Test)] = "rpc_debugging"
    };
    public Dictionary<string, string> ExternalImageUrls { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rpc_codex"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/codex.png",
        ["rpc_building"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/building.gif",
        ["rpc_coding"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/coding.gif",
        ["rpc_debugging"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/debugging.gif",
        ["rpc_deploying"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/deploying.gif",
        ["rpc_error"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/error.gif",
        ["rpc_reading"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/reading.gif",
        ["rpc_searching"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/searching.gif",
        ["rpc_sleeping"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/sleeping.gif",
        ["rpc_success"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/success.gif",
        ["rpc_thinking"] = "https://raw.githubusercontent.com/SSHdotCodes/codex-rpc/1a44161a554c4de584c0af7d5eb47c4545983410/assets/thinking.gif"
    };
}

public sealed class CodexDetectionOptions
{
    public string? HomePath { get; set; }
    public string[] ModelEnvironmentVariables { get; set; } =
    [
        "CODEX_MODEL",
        "OPENAI_MODEL",
        "MODEL_NAME"
    ];
    public string[] ProcessNameContains { get; set; } = ["codex"];
    public string[] WindowTitleContains { get; set; } = ["Codex"];
    public string[] ExecutablePathContains { get; set; } = [];
    public string[] CommandLineContains { get; set; } = [];
    public int RecentSessionFilesToScan { get; set; } = 20;

    public string GetResolvedHomePath()
    {
        if (!string.IsNullOrWhiteSpace(HomePath))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(HomePath));
        }

        var envCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(envCodexHome))
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(envCodexHome));
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, ".codex");
    }
}

public sealed class ProjectOptions
{
    public string Path { get; set; } = ".";
    public string? DisplayName { get; set; }
    public bool PreferGitRootForProjectPath { get; set; } = true;
    public int RecentFileSearchDepth { get; set; } = 6;
    public int MaxRecentEditedFilesToTrack { get; set; } = 6;
    public int MaxProjectFilesToScan { get; set; } = 5000;
    public long MaxLineCountFileBytes { get; set; } = 1_000_000;
    public string[] IgnoredFilePatterns { get; set; } =
    [
        "*.log",
        "*.pid",
        "*.tmp",
        "*.user",
        "*.suo",
        "*.png",
        "*.jpg",
        "*.jpeg",
        "*.gif",
        "*.ico",
        "*.dll",
        "*.exe",
        "*.pdb",
        "*.zip"
    ];
    public string[] IgnoredDirectories { get; set; } =
    [
        ".git",
        ".vs",
        ".vscode",
        "bin",
        "obj",
        "node_modules",
        "dist",
        "build",
        "out",
        "artifacts",
        "publish",
        "publish-latest"
    ];
}

public sealed class PresenceTemplateOptions
{
    public bool AutoDetectModelName { get; set; } = true;
    public string ModelName { get; set; } = "Codex";
    public string Details { get; set; } = "{GoalModePrefix} {ModelName} \u2022 {Tokens}";
    public string State { get; set; } = "{ActivityLine}";
    public bool EnableLargeImageText { get; set; } = true;
    public string LargeImageText { get; set; } = "{ProjectName}";
    public string SmallImageText { get; set; } = "{ProjectFileCount} files \u2022 session {SessionElapsed}";
    public PresenceButtonOptions[] Buttons { get; set; } = [];
    public string AnalyzingProjectText { get; set; } = "Thinking";
    public string CoordinatingChangesText { get; set; } = "Coordinating {n} files";
    public string CreatingFilesText { get; set; } = "Creating files";
    public string DeletingFilesText { get; set; } = "Deleting files";
    public string RunningCommandText { get; set; } = "Run Command";
    public string PlanningText { get; set; } = "Planning";
    public string ApplyingEditsText { get; set; } = "Editing";
    public string RefactoringText { get; set; } = "Refactoring";
    public string ReadingText { get; set; } = "Reading";
    public string StalledText { get; set; } = "Stalled";
    public string ThinkingText { get; set; } = "Thinking";
    public string WorkingText { get; set; } = "Working";
    public string InvestigatingText { get; set; } = "Investigating";
    public string IdlingText { get; set; } = "Idling";
    public string ReadyText { get; set; } = "Hold on";
    public string AnalyzingText { get; set; } = "Thinking";
    public string WaitingText { get; set; } = "Waiting";
    public string ReadyActivityText { get; set; } = "Idling";
    public string WaitingActivityText { get; set; } = "Waiting";
    public string OfflineText { get; set; } = "Idling";
    public int ThinkingStaleTimeoutMinutes { get; set; } = 10;
    public int ReadyIdleGraceMinutes { get; set; } = 5;
    public int EditingFreshnessSeconds { get; set; } = 12;
    public int ActiveUpdateIntervalSeconds { get; set; } = 1;
    public int RunningCommandUpdateIntervalSeconds { get; set; } = 1;
    public int RunningCommandUpdateIntervalMilliseconds { get; set; } = 500;
    public int RunningCommandHoldSeconds { get; set; } = 2;
    public int IdleUpdateIntervalSeconds { get; set; } = 8;
}

public sealed class PresenceButtonOptions
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class TokenUsageOptions
{
    public bool Enabled { get; set; } = true;
    public long? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
}


