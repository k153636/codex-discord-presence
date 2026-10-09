namespace CodexDiscordPresence;

internal readonly record struct DashboardMetric(string Label, string Value, double? ProgressPercent = null);
