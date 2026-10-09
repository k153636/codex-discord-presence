using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed record ClaudeCodeUsageObservation(
    string SessionId,
    string ProjectPath,
    DateTimeOffset ObservedAtUtc,
    decimal? EstimatedCostUsd,
    bool HasSubscriptionUsage,
    RateLimitSnapshot? RateLimit)
{
    internal static ClaudeCodeUsageObservation? Parse(string json, DateTimeOffset nowUtc)
    {
        if (System.Text.Encoding.UTF8.GetByteCount(json) > ClaudeCodeHookParser.MaxPayloadBytes) return null;
        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                ClaudeCodeHookParser.Text(root, "session_id") is not { } sessionId ||
                ClaudeCodeHookParser.SafeText(sessionId) is not { } safeSessionId)
                return null;
            var projectPath = root.TryGetProperty("workspace", out var workspace) && workspace.ValueKind == JsonValueKind.Object
                ? ClaudeCodeHookParser.Text(workspace, "current_dir") : null;
            projectPath ??= ClaudeCodeHookParser.Text(root, "cwd");
            if (ClaudeCodeHookParser.SafeText(projectPath, 4096) is not { } safePath || !Path.IsPathFullyQualified(safePath))
                return null;

            decimal? cost = root.TryGetProperty("cost", out var costs) && costs.ValueKind == JsonValueKind.Object &&
                costs.TryGetProperty("total_cost_usd", out var amount) && amount.ValueKind == JsonValueKind.Number &&
                amount.TryGetDecimal(out var value) && value >= 0 ? value : null;
            var rateLimit = ReadWindow(root, "five_hour", 300);
            var hasSubscriptionUsage = rateLimit is not null || ReadWindow(root, "seven_day", 10080) is not null;
            return new(safeSessionId, safePath, nowUtc.ToUniversalTime(), cost, hasSubscriptionUsage, rateLimit);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    private static RateLimitSnapshot? ReadWindow(JsonElement root, string name, int durationMinutes)
    {
        if (!root.TryGetProperty("rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object ||
            !limits.TryGetProperty(name, out var window) || window.ValueKind != JsonValueKind.Object ||
            !window.TryGetProperty("used_percentage", out var percentage) || percentage.ValueKind != JsonValueKind.Number ||
            !percentage.TryGetDecimal(out var used) || used is < 0 or > 100 ||
            !window.TryGetProperty("resets_at", out var reset) || reset.ValueKind != JsonValueKind.Number ||
            !reset.TryGetInt64(out var seconds)) return null;
        return new((int)Math.Round(used, MidpointRounding.AwayFromZero), durationMinutes,
            DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime);
    }
}
