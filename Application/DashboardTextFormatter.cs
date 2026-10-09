using System.Globalization;
using DiscordRPC;

namespace CodexDiscordPresence;

internal static class DashboardTextFormatter
{
    public static string FormatProviderName(string? providerId) => providerId switch
    {
        ProviderIds.Codex => "Codex",
        ProviderIds.ClaudeCode => "Claude Code",
        ProviderIds.Antigravity => "Antigravity CLI",
        _ => "No active provider"
    };

    public static string FormatConnection(PresenceDashboardSnapshot snapshot) => snapshot.IsDiscordConnected
        ? "Connected" : snapshot.IsDiscordConnecting ? "Connecting" : "Disconnected";

    public static (string Title, string Description) FormatEmptyPreview(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        if (!enabled) return ("Presence disabled", "Enable Discord Rich Presence from the tray menu.");
        if (snapshot.HasNoActiveProvider) return ("No active coding client", "Start an enabled client to publish an activity.");
        if (!snapshot.IsDiscordConnected)
            return (snapshot.IsDiscordConnecting ? "Connecting to Discord" : "Discord disconnected", "No activity has been acknowledged by Discord.");
        return ("No published presence", "Waiting for Discord to acknowledge an activity.");
    }

    public static DashboardMetric[] CreateMetrics(PresenceDashboardSnapshot snapshot, DateTime utcNow)
    {
        var usage = snapshot.TokenUsage;
        var metrics = new List<DashboardMetric>();
        var accent = DashboardProviderPresentation.Accent(snapshot.ProviderId);
        if (snapshot.HasNoActiveProvider) return [];
        if (snapshot.ProviderId == ProviderIds.Codex)
        {
            var billing = FormatBillingType(usage?.BillingType);
            if (billing.Length > 0) metrics.Add(new("Billing", billing, accent));
            if (usage?.RateLimit is { WindowDurationMinutes: 300 } limit)
            {
                metrics.Add(new("5h limit", $"{limit.UsedPercent}% used", accent, limit.UsedPercent));
                metrics.Add(new("Reset in", FormatRemaining(limit.ResetAtUtc, utcNow), accent));
            }
        }
        else if (snapshot.ProviderId == ProviderIds.Antigravity)
        {
            if (!string.IsNullOrWhiteSpace(usage?.PlanName)) metrics.Add(new("Plan", usage.PlanName, accent));
            var quota = usage?.UsageQuotas?.Where(item => item.RemainingFraction is >= 0 and <= 1)
                .OrderBy(item => item.RemainingFraction).FirstOrDefault();
            if (quota is not null)
            {
                var percent = (double)(quota.RemainingFraction * 100);
                metrics.Add(new("Quota left", percent.ToString("0.#", CultureInfo.InvariantCulture) + "%", accent, percent));
                if (quota.ResetAtUtc is { } reset) metrics.Add(new("Reset in", FormatRemaining(reset.UtcDateTime, utcNow), accent));
            }
        }
        return metrics.ToArray();
    }

    public static string FormatUsageNote(PresenceDashboardSnapshot snapshot) => snapshot.HasNoActiveProvider
        ? "No current provider activity."
        : snapshot.ProviderId switch
        {
            ProviderIds.ClaudeCode => "Claude Code does not report usage or billing to this app.",
            ProviderIds.Codex => "Usage is not available for this session.",
            ProviderIds.Antigravity => "Plan and quota are not available for this session.",
            _ => "No current provider activity."
        };

    private static string FormatRemaining(DateTime reset, DateTime utcNow)
    {
        var minutes = (long)Math.Max(0, Math.Ceiling((reset - utcNow).TotalMinutes));
        return $"{minutes / 60}h {minutes % 60}m";
    }

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

        var state = snapshot.PublishedPresence?.State;

        if (!string.IsNullOrWhiteSpace(state))
        {
            return state;
        }

        return string.Empty;
    }
}
