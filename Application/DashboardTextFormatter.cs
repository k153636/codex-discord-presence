using System.Globalization;
using DiscordRPC;

namespace CodexDiscordPresence;

internal static class DashboardTextFormatter
{
    public static string FormatActivityType(DiscordPresenceSnapshot? presence)
    {
        return presence?.ActivityType switch
        {
            ActivityType.Playing => "Playing:",
            ActivityType.Listening => "Listening to:",
            ActivityType.Watching => "Watching:",
            ActivityType.Competing => "Competing in:",
            _ => string.Empty
        };
    }

    public static string FormatBillingType(string? billingType)
    {
        return billingType?.Trim().ToLowerInvariant() switch
        {
            "api" or "apikey" => "API",
            "subsc" or "subscription" or "chatgpt" or "free" or "go" or "plus" or "pro" or "business" or "enterprise" or "edu" => "subsc",
            _ => string.Empty
        };
    }

    public static string FormatRateLimitUsage(RateLimitSnapshot? rateLimit)
    {
        return rateLimit is { WindowDurationMinutes: 300 }
            ? $"5h {rateLimit.UsedPercent.ToString(CultureInfo.InvariantCulture)}% used"
            : string.Empty;
    }

    public static string FormatRateLimitReset(RateLimitSnapshot? rateLimit, DateTime utcNow)
    {
        if (rateLimit is not { WindowDurationMinutes: 300 })
        {
            return string.Empty;
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

        var totalSeconds = Math.Max(0L, (long)elapsed.TotalSeconds);
        var totalMinutes = totalSeconds / 60;
        var seconds = totalSeconds % 60;

        return totalMinutes >= 60
            ? $"{totalMinutes / 60}:{totalMinutes % 60:00}:{seconds:00}"
            : $"{totalMinutes}:{seconds:00}";
    }

    public static string FormatActivity(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        if (!enabled)
        {
            return "Disabled";
        }

        var state = snapshot.PublishedPresence is not null
            ? snapshot.PublishedPresence.State
            : snapshot.Presence?.State;

        if (!string.IsNullOrWhiteSpace(state))
        {
            return state;
        }

        return string.Empty;
    }
}
