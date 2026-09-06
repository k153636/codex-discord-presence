namespace CodexDiscordPresence;

internal static class ThinkingSummaryFormatter
{
    private const int MaxPresenceLength = 96;

    public static string? Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value
            .Replace("**", "", StringComparison.Ordinal)
            .Replace("__", "", StringComparison.Ordinal)
            .Replace("`", "", StringComparison.Ordinal)
            .ReplaceLineEndings(" ");
        normalized = string.Join(
            ' ',
            normalized.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries));

        return string.IsNullOrWhiteSpace(normalized)
            ? null
            : normalized.Trim();
    }

    public static string? FormatForPresence(string? value)
    {
        var normalized = Normalize(value);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return null;
        }

        return normalized.Length <= MaxPresenceLength
            ? normalized
            : normalized[..(MaxPresenceLength - 1)] + "…";
    }
}
