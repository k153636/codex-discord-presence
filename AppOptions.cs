using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class AppOptions
{
    public DiscordOptions Discord { get; set; } = new();
    public CodexDetectionOptions Codex { get; set; } = new();
    public ProjectOptions Project { get; set; } = new();
    public PresenceTemplateOptions Presence { get; set; } = new();
    public TokenUsageOptions TokenUsage { get; set; } = new();
    public int UpdateIntervalSeconds { get; set; } = 2;

    public static AppOptions Load(string[] args)
    {
        var path = "appsettings.json";
        var options = File.Exists(path)
            ? JsonSerializer.Deserialize<AppOptions>(File.ReadAllText(path), JsonOptions()) ?? new AppOptions()
            : new AppOptions();

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
    public string? LargeImageKey { get; set; } = "codex_logo";
    public string? SmallImageKey { get; set; }
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
        "build"
    ];
}

public sealed class PresenceTemplateOptions
{
    public bool AutoDetectModelName { get; set; } = true;
    public string ModelName { get; set; } = "Codex";
    public string Details { get; set; } = "{ModelName} \u2022 {Tokens}";
    public string State { get; set; } = "{ActivityLine}";
    public string LargeImageText { get; set; } = "working on {ProjectName}";
    public string SmallImageText { get; set; } = "{ProjectFileCount} files \u2022 {FreshnessText}";
    public PresenceButtonOptions[] Buttons { get; set; } = [];
    public string AnalyzingProjectText { get; set; } = "Thinking";
    public string UpdatingFilesText { get; set; } = "Coordinating changes across {n} files";
    public string CreatingFilesText { get; set; } = "Creating files";
    public string DeletingFilesText { get; set; } = "Deleting files";
    public string RunningCommandText { get; set; } = "Running command";
    public string PlanningText { get; set; } = "Planning";
    public string ApplyingEditsText { get; set; } = "Applying edits";
    public string RefactoringText { get; set; } = "Refactoring";
    public string ThinkingText { get; set; } = "Thinking";
    public string IdlingText { get; set; } = "Idling";
    public string ReadyText { get; set; } = "Hold on";
    public string AnalyzingText { get; set; } = "Thinking";
    public string WaitingText { get; set; } = "Idling";
    public string ReadyActivityText { get; set; } = "Idling";
    public string WaitingActivityText { get; set; } = "Idling";
    public string OfflineText { get; set; } = "Idling";
    public string FreshnessText { get; set; } = "updated {FreshnessElapsed} ago";
    public int ThinkingStaleTimeoutMinutes { get; set; } = 10;
    public int ReadyIdleGraceMinutes { get; set; } = 5;
    public int EditingFreshnessSeconds { get; set; } = 12;
    public int FreshnessUpdateIntervalSeconds { get; set; } = 3;
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


