namespace CodexDiscordPresence;

internal static class PartyActivityComposer
{
    private const string LegacyMainAgentPrefix = "Main agent ";

    public static string AddPartyPrefix(PresenceContext context, string activityLine)
    {
        if (string.IsNullOrWhiteSpace(activityLine))
        {
            return activityLine;
        }

        var normalizedActivityLine = activityLine.StartsWith(LegacyMainAgentPrefix, StringComparison.Ordinal)
            ? activityLine[LegacyMainAgentPrefix.Length..]
            : activityLine;

        if (context.Codex.PartySize is not > 1)
        {
            return normalizedActivityLine;
        }

        var partyLabel = $"{context.Codex.PartySize}/{context.Codex.PartySize} ";
        return normalizedActivityLine.StartsWith(partyLabel, StringComparison.Ordinal)
            ? normalizedActivityLine
            : partyLabel + normalizedActivityLine;
    }
}
