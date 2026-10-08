using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

internal static partial class ClaudeCodeSpinnerLabel
{
    internal static string? ParseScreen(string screen)
    {
        var matches = SpinnerRow().Matches(screen);
        return matches.Count == 1 ? matches[0].Groups[1].Value.Trim() : null;
    }

    [GeneratedRegex(@"(?m)^\s*[✻✽✶✳✢·*]\s+([A-Za-z][A-Za-z -]{1,47})(?:…|\.\.\.)\s+\(\d+(?:s|m|h)\b", RegexOptions.CultureInvariant)]
    private static partial Regex SpinnerRow();
}
