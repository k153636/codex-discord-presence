namespace CodexDiscordPresence.Tests;

public sealed class CodexAgentActivityTrackerTests
{
    [Fact]
    public void GetActiveAgentThreadIds_DeduplicatesStartsAndRemovesCompletedAgents()
    {
        CodexActivityEvent[] events =
        [
            Event(1, CodexActivityEventKind.TurnStarted),
            Event(2, CodexActivityEventKind.AgentStarted, "agent-2", "agent-1"),
            Event(3, CodexActivityEventKind.AgentStarted, "agent-1"),
            Event(4, CodexActivityEventKind.AgentCompleted, "agent-2")
        ];

        var active = CodexAgentActivityTracker.GetActiveAgentThreadIds(events);

        Assert.Equal(["agent-1"], active);
    }

    [Fact]
    public void GetActiveAgentThreadIds_ClearsAgentsWhenTurnEnds()
    {
        CodexActivityEvent[] events =
        [
            Event(1, CodexActivityEventKind.TurnStarted),
            Event(2, CodexActivityEventKind.AgentStarted, "agent-1"),
            Event(3, CodexActivityEventKind.TurnCompleted),
            Event(4, CodexActivityEventKind.AgentCompleted, "agent-1")
        ];

        var active = CodexAgentActivityTracker.GetActiveAgentThreadIds(events);

        Assert.Empty(active);
    }

    private static CodexActivityEvent Event(
        long sequence,
        CodexActivityEventKind kind,
        params string[] agentThreadIds)
    {
        return new CodexActivityEvent
        {
            Sequence = sequence,
            TimestampUtc = DateTime.UtcNow.AddSeconds(sequence),
            Kind = kind,
            AgentThreadIds = agentThreadIds
        };
    }
}
