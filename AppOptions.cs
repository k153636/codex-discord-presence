using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class AppOptions
{
    public DiscordOptions Discord { get; set; } = new();
    public CodexDetectionOptions Codex { get; set; } = new();
    public ProjectOptions Project { get; set; } = new();
    public PresenceTemplateOptions Presence { get; set; } = new();
    public TokenUsageOptions TokenUsage { get; set; } = new();
    public int UpdateIntervalSeconds { get; set; } = 15;

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
    public string ClientId { get; set; } = "1516774220636360784";
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
    public int RecentFileSearchDepth { get; set; } = 6;
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
    public string Details { get; set; } = "{ModelName} working on {ProjectName}";
    public string State { get; set; } = "{ActivityLine}";
    public string LargeImageText { get; set; } = "{CodexStatus} ・ session {SessionElapsed}";
    public string SmallImageText { get; set; } = "{Tokens} ・ est. {EstimatedCost}";
    public PresenceButtonOptions[] Buttons { get; set; } = [];
    public string ThinkingText { get; set; } = "Thinking";
    public string WaitingText { get; set; } = "waiting";
    public string OfflineText { get; set; } = "Offline";
    public int ThinkingStaleTimeoutMinutes { get; set; } = 10;
}

public sealed class PresenceButtonOptions
{
    public string Label { get; set; } = "";
    public string Url { get; set; } = "";
}

public sealed class TokenUsageOptions
{
    public bool Enabled { get; set; }
    public long? TotalTokens { get; set; }
    public decimal? EstimatedCostUsd { get; set; }
}
