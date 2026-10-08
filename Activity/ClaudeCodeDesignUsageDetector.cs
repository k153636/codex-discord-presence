using System.Text.Json;

namespace CodexDiscordPresence;

// Recognize execution targets, never request text or generic design skills.
internal static class ClaudeCodeDesignUsageDetector
{
    internal static bool IsDesignOperation(string? toolName, JsonElement input)
    {
        if (toolName != "Artifact" || input.ValueKind != JsonValueKind.Object)
        {
            return false;
        }
        return ClaudeCodeHookParser.Text(input, "action") == "quickstart" &&
                ClaudeCodeHookParser.Text(input, "intent") == "design" ||
            ClaudeCodeHookParser.Text(input, "file_path", 4096) is { } file &&
                file.EndsWith(".dc.html", StringComparison.OrdinalIgnoreCase);
    }
}
