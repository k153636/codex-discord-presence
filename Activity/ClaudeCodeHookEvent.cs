using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed record ClaudeCodeHookEvent(
    string SessionId,
    string ProjectPath,
    string EventName,
    DateTimeOffset ObservedAtUtc,
    string? ToolName = null,
    string? ToolUseId = null,
    string? FileName = null,
    string? AgentId = null,
    string? Model = null,
    string? TranscriptPath = null,
    string? NotificationType = null,
    bool IsClaudeDesignOperation = false);

internal static class ClaudeCodeHookParser
{
    internal const int MaxPayloadBytes = 1_048_576;
    internal static readonly string[] EventNames =
    [
        "SessionStart", "UserPromptSubmit", "PreToolUse", "PostToolUse",
        "PostToolUseFailure", "PermissionRequest", "Notification", "Stop",
        "SessionEnd", "SubagentStart", "SubagentStop"
    ];

    internal static ClaudeCodeHookEvent? Parse(string json, DateTimeOffset nowUtc)
    {
        if (json.Length > MaxPayloadBytes)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var sessionId = Text(root, "session_id");
            var cwd = Text(root, "cwd", 4096);
            var eventName = Text(root, "hook_event_name");
            if (sessionId is null || cwd is null || !Path.IsPathFullyQualified(cwd) ||
                eventName is null || !EventNames.Contains(eventName, StringComparer.Ordinal))
            {
                return null;
            }

            var agentId = Text(root, "agent_id");
            if (agentId is not null && eventName is not (
                "SubagentStart" or "SubagentStop" or "PreToolUse" or "PostToolUse" or "PostToolUseFailure"))
            {
                return null;
            }
            if (eventName == "Notification" && Text(root, "notification_type") is not ("permission_prompt" or "idle_prompt"))
            {
                return null;
            }

            var file = root.TryGetProperty("tool_input", out var input) && input.ValueKind == JsonValueKind.Object
                ? Text(input, "file_path", 4096) ?? Text(input, "notebook_path", 4096)
                : null;
            // Store no prompts, commands, tool responses, or source contents.
            var fileName = agentId is null
                ? file?.Replace('\\', '/').Split('/').LastOrDefault()
                : null;
            return new ClaudeCodeHookEvent(
                sessionId, Path.GetFullPath(cwd), eventName, nowUtc,
                Text(root, "tool_name"), Text(root, "tool_use_id"),
                SafeText(fileName), agentId, Text(root, "model"),
                Text(root, "transcript_path", 4096), Text(root, "notification_type"),
                ClaudeCodeDesignUsageDetector.IsDesignOperation(Text(root, "tool_name"), input));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    internal static string? Text(JsonElement root, string name, int maxLength = 256)
    {
        return root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String
            ? SafeText(value.GetString(), maxLength)
            : null;
    }

    internal static string? SafeText(string? value, int maxLength = 256)
    {
        return string.IsNullOrWhiteSpace(value) || value.Length > maxLength || value.Any(char.IsControl)
            ? null
            : value.Trim();
    }
}
