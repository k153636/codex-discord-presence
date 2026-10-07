using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed record ClaudeCodeTranscriptMetadata(string? Model, string? Effort)
{
    internal static ClaudeCodeTranscriptMetadata Read(string? path, string sessionId)
    {
        string? model = null;
        string? effort = null;
        if (path is null || !Path.IsPathFullyQualified(path) || Path.GetExtension(path) != ".jsonl")
        {
            return new(null, null);
        }
        try
        {
            foreach (var line in ClaudeCodeTranscriptTail.ReadLines(path))
            {
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (ClaudeCodeHookParser.Text(root, "sessionId") != sessionId ||
                        root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True ||
                        ClaudeCodeHookParser.Text(root, "type") != "assistant" ||
                        !root.TryGetProperty("message", out var message))
                    {
                        continue;
                    }
                    model = ClaudeCodeHookParser.Text(message, "model") ?? model;
                    var observedEffort = ClaudeCodeHookParser.Text(root, "effort");
                    effort = observedEffort is "low" or "medium" or "high" or "max" ? observedEffort : null;
                }
                catch (JsonException)
                {
                    // A currently appended line may be incomplete.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new(null, null);
        }
        return new(model, effort);
    }
}
