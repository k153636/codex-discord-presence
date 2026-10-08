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

        if (context.Activity.PartySize is not > 1)
        {
            return normalizedActivityLine;
        }

        var partyLabel = $"{context.Activity.PartySize}/{context.Activity.PartySize} ";
        return normalizedActivityLine.StartsWith(partyLabel, StringComparison.Ordinal)
            ? normalizedActivityLine
            : partyLabel + normalizedActivityLine;
    }
}
