namespace CodexDiscordPresence;

internal static class CodexAgentActivityTracker
{
    public static IReadOnlyList<string> GetActiveAgentThreadIds(IEnumerable<CodexActivityEvent> events)
    {
        var activeThreadIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var activityEvent in events
            .OrderBy(activityEvent => activityEvent.Sequence)
            .ThenBy(activityEvent => activityEvent.TimestampUtc))
        {
            switch (activityEvent.Kind)
            {
                case CodexActivityEventKind.TurnStarted:
                    activeThreadIds.Clear();
                    break;

                case CodexActivityEventKind.AgentStarted:
                    foreach (var threadId in activityEvent.AgentThreadIds)
                    {
                        if (!string.IsNullOrWhiteSpace(threadId))
                        {
                            activeThreadIds.Add(threadId.Trim());
                        }
                    }

                    break;

                case CodexActivityEventKind.AgentCompleted:
                    foreach (var threadId in activityEvent.AgentThreadIds)
                    {
                        if (!string.IsNullOrWhiteSpace(threadId))
                        {
                            activeThreadIds.Remove(threadId.Trim());
                        }
                    }

                    break;

                case CodexActivityEventKind.TurnCompleted:
                case CodexActivityEventKind.TurnFailed:
                case CodexActivityEventKind.TurnInterrupted:
                    activeThreadIds.Clear();
                    break;
            }
        }

        return activeThreadIds
            .OrderBy(threadId => threadId, StringComparer.Ordinal)
            .ToArray();
    }
}
