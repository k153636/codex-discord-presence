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

    public static string? FormatForCurrentReasoning(
        string? value,
        CodexActivityEventKind? latestActivityEventKind)
    {
        // The state machine keeps the latest summary for the current turn while
        // Codex transitions through a completed operation or resolved input.
        // Only a new operation, an unresolved input, or a terminal event makes
        // the previous summary ineligible for the current presence line.
        if (latestActivityEventKind is not null &&
            latestActivityEventKind is not (
                CodexActivityEventKind.TurnStarted or
                CodexActivityEventKind.Reasoning or
                CodexActivityEventKind.OperationCompleted or
                CodexActivityEventKind.InputResolved))
        {
            return null;
        }

        return FormatForPresence(value);
    }
}
