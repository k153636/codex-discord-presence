using System.Text.Json.Nodes;

namespace CodexDiscordPresence;

internal sealed record AntigravityHookCommandBuildResult(
    bool IsSupported,
    IReadOnlyDictionary<string, string>? Commands,
    string? ScriptContent,
    string? Error);

internal interface IAntigravityHookCommandBuilder
{
    AntigravityHookCommandBuildResult Build(AntigravityHookPaths paths);
}

internal sealed class AntigravityHookCommandBuilder : IAntigravityHookCommandBuilder
{
    private readonly AntigravityStatusLinePlatform _platform;

    public AntigravityHookCommandBuilder()
        : this(OperatingSystem.IsWindows()
            ? AntigravityStatusLinePlatform.Windows
            : AntigravityStatusLinePlatform.Unsupported)
    {
    }

    internal AntigravityHookCommandBuilder(AntigravityStatusLinePlatform platform)
    {
        _platform = platform;
    }

    public AntigravityHookCommandBuildResult Build(AntigravityHookPaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_platform != AntigravityStatusLinePlatform.Windows)
        {
            return new(
                IsSupported: false,
                Commands: null,
                ScriptContent: null,
                Error: "Antigravity hook integration is supported on Windows only.");
        }

        var commands = AntigravityHookEvents.All.ToDictionary(
            hookEvent => hookEvent,
            hookEvent => AntigravityPowerShellCommand.BuildEncoded(
                paths.ScriptPath,
                "-HookEvent",
                hookEvent),
            StringComparer.Ordinal);
        return new(
            IsSupported: true,
            Commands: commands,
            ScriptContent: AntigravityEventPowerShellScript.Create(paths.EventFilePath),
            Error: null);
    }

    internal static JsonObject CreateManagedDefinition(
        IReadOnlyDictionary<string, string> commands) => new()
        {
            ["PreInvocation"] = new JsonArray(
                CreateCommand(commands, AntigravityHookEvents.PreInvocation)),
            ["PostToolUse"] = new JsonArray(
                new JsonObject
                {
                    ["matcher"] = "*",
                    ["hooks"] = new JsonArray(
                        CreateCommand(commands, AntigravityHookEvents.PostToolUse))
                }),
            ["PostInvocation"] = new JsonArray(
                CreateCommand(commands, AntigravityHookEvents.PostInvocation)),
            ["Stop"] = new JsonArray(
                CreateCommand(commands, AntigravityHookEvents.Stop))
        };

    private static JsonObject CreateCommand(
        IReadOnlyDictionary<string, string> commands,
        string hookEvent) => new()
        {
            ["type"] = "command",
            ["command"] = commands[hookEvent],
            ["timeout"] = 10
        };
}

internal static class AntigravityHookEvents
{
    internal const string PreInvocation = "PreInvocation";
    internal const string PostToolUse = "PostToolUse";
    internal const string PostInvocation = "PostInvocation";
    internal const string Stop = "Stop";

    internal static IReadOnlyList<string> All { get; } =
    [
        PreInvocation,
        PostToolUse,
        PostInvocation,
        Stop
    ];
}
