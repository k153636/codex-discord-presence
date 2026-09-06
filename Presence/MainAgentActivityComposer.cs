namespace CodexDiscordPresence;

internal static class MainAgentActivityComposer
{
    private const string Prefix = "Main agent ";

    public static string AddRole(PresenceContext context, string activityLine)
    {
        if (string.IsNullOrWhiteSpace(activityLine) || context.Codex.PartySize is not > 1)
        {
            return activityLine;
        }

        return activityLine.StartsWith(Prefix, StringComparison.Ordinal)
            ? activityLine
            : Prefix + activityLine;
    }
}
