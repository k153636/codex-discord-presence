namespace CodexDiscordPresence;

internal sealed record ClaudeCodeToolObservation(string Id, string Name, string? FileName,
    bool IsClaudeDesignOperation = false);

internal sealed record ClaudeCodeSessionObservation(
    string SessionId,
    string ProjectPath,
    DateTimeOffset ObservedAtUtc,
    DateTimeOffset ActivityStartedAtUtc,
    string EventName,
    bool Ended,
    string? Model,
    string? TranscriptPath,
    IReadOnlyList<ClaudeCodeToolObservation> Tools,
    IReadOnlyList<string> ActiveAgentIds)
{
    public bool FromTranscript { get; init; }
    public DateTimeOffset? LastActivityEventAtUtc { get; init; }
    public bool HasConfirmedClaudeDesignUsage { get; init; }
    public bool UsesClaudeDesign => HasConfirmedClaudeDesignUsage || Tools.Any(tool => tool.IsClaudeDesignOperation);

    internal static ClaudeCodeSessionObservation Apply(
        ClaudeCodeSessionObservation? previous,
        ClaudeCodeHookEvent current)
    {
        if (previous is not null && previous.SessionId == current.SessionId &&
            (current.ObservedAtUtc < previous.ObservedAtUtc ||
             previous.Ended && current.EventName != "SessionStart"))
        {
            return previous;
        }
        var reset = previous is null || previous.SessionId != current.SessionId ||
            previous.Ended || current.EventName == "SessionStart";
        var tools = reset ? new List<ClaudeCodeToolObservation>() : previous!.Tools.ToList();
        var agents = reset ? new HashSet<string>(StringComparer.Ordinal) : previous!.ActiveAgentIds.ToHashSet(StringComparer.Ordinal);
        var eventName = current.EventName;
        var confirmedDesign = !reset && previous!.HasConfirmedClaudeDesignUsage;
        if (eventName == "PreToolUse" && current.ToolName is { } toolName && tools.Count < 64)
        {
            var id = current.ToolUseId ?? toolName;
            tools.RemoveAll(tool => tool.Id == id);
            tools.Add(new ClaudeCodeToolObservation(id, toolName, current.FileName, current.IsClaudeDesignOperation));
        }
        else if (eventName is "PostToolUse" or "PostToolUseFailure")
        {
            if (eventName == "PostToolUse")
            {
                confirmedDesign |= current.IsClaudeDesignOperation || tools.Any(tool => tool.IsClaudeDesignOperation &&
                    (current.ToolUseId is { } completedId ? tool.Id == completedId : tool.Name == current.ToolName));
            }
            tools.RemoveAll(tool => current.ToolUseId is { } id
                ? tool.Id == id
                : tool.Name == current.ToolName);
        }
        else if (eventName == "SubagentStart" && current.AgentId is { } agentId && agents.Count < 64)
        {
            agents.Add(agentId);
        }
        else if (eventName == "SubagentStop" && current.AgentId is { } stoppedId)
        {
            agents.Remove(stoppedId);
        }

        // Subagent lifecycle updates party evidence without replacing main-agent activity.
        if (eventName is "SubagentStart" or "SubagentStop")
        {
            eventName = reset ? "SessionStart" : previous!.EventName;
        }
        if (current.EventName is "UserPromptSubmit" or "Stop" or "SessionEnd")
        {
            tools.Clear();
            confirmedDesign = false;
        }
        if (current.EventName == "SessionEnd")
        {
            agents.Clear();
        }

        var activityStartedAt = !reset && eventName == previous!.EventName &&
            tools.SequenceEqual(previous.Tools)
            ? previous.ActivityStartedAtUtc
            : current.ObservedAtUtc;
        return new ClaudeCodeSessionObservation(
            current.SessionId, current.ProjectPath, current.ObservedAtUtc, activityStartedAt,
            eventName, current.EventName == "SessionEnd",
            current.Model ?? (reset ? null : previous!.Model),
            current.TranscriptPath ?? (reset ? null : previous!.TranscriptPath),
            tools.ToArray(), agents.Order(StringComparer.Ordinal).ToArray())
        {
            HasConfirmedClaudeDesignUsage = confirmedDesign,
            LastActivityEventAtUtc = !reset && current.EventName is "SubagentStart" or "SubagentStop"
                ? previous!.LastActivityEventAtUtc ?? previous.ObservedAtUtc
                : current.ObservedAtUtc
        };
    }
}
