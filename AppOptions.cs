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
    public string ClientId { get; set; } = "YOUR_DISCORD_APPLICATION_CLIENT_ID";
    public string? LargeImageKey { get; set; }
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
    public string State { get; set; } = "Working on {ProjectName}";
    public string LargeImageText { get; set; } = "{CodexStatus} ・ {SessionElapsed}";
    public string SmallImageText { get; set; } = "Editing: {EditingFileName}";
    public PresenceButtonOptions[] Buttons { get; set; } = [];
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
