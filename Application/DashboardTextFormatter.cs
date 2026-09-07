using System.Globalization;

namespace CodexDiscordPresence;

internal static class DashboardTextFormatter
{
    public static string FormatBillingType(string? billingType)
    {
        return billingType?.Trim().ToLowerInvariant() switch
        {
            "api" or "apikey" => "API",
            "subsc" or "subscription" or "chatgpt" or "free" or "go" or "plus" or "pro" or "business" or "enterprise" or "edu" => "subsc",
            _ => "unavailable"
        };
    }

    public static string FormatRateLimitUsage(RateLimitSnapshot? rateLimit)
    {
        return rateLimit is { WindowDurationMinutes: 300 }
            ? $"5h {rateLimit.UsedPercent.ToString(CultureInfo.InvariantCulture)}% used"
            : "5h unavailable";
    }

    public static string FormatRateLimitReset(RateLimitSnapshot? rateLimit, DateTime utcNow)
    {
        if (rateLimit is not { WindowDurationMinutes: 300 })
        {
            return "reset unavailable";
        }

        var remaining = rateLimit.ResetAtUtc - utcNow;
        var resetMinutes = remaining <= TimeSpan.Zero
            ? 0L
            : (long)Math.Ceiling(remaining.TotalMinutes);

        return $"reset {resetMinutes / 60}h {resetMinutes % 60}m";
    }

    public static string FormatElapsed(DateTime? startedAtUtc, DateTime utcNow)
    {
        if (!startedAtUtc.HasValue)
        {
            return "";
        }

        var elapsed = utcNow - startedAtUtc.Value;
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        var totalHours = (int)elapsed.TotalHours;
        return $"{totalHours}:{elapsed.Minutes:00}";
    }

    public static string FormatActivity(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        if (!enabled)
        {
            return "Disabled";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.Presence?.State))
        {
            return snapshot.Presence.State;
        }

        return "Waiting";
    }

    public static string FormatModelProject(PresenceDashboardSnapshot snapshot)
    {
        var model = snapshot.ModelName?.Trim() ?? "";
        var project = snapshot.ProjectName?.Trim() ?? "";

        if (model.Length > 0 && project.Length > 0)
        {
            return $"{model} working on {project}";
        }

        return model.Length > 0 ? model : project;
    }
}
