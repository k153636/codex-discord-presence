namespace CodexDiscordPresence;

internal sealed record ClaudeCodeToolObservation(string Id, string Name, string? FileName,
    bool IsClaudeDesignOperation = false);

internal sealed record ClaudeCodeSubagentToolObservation(
    string AgentId,
    string ToolUseId,
    SubagentWorkKind WorkKind,
    DateTimeOffset ObservedAtUtc);

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
    public IReadOnlyList<ClaudeCodeSubagentToolObservation> SubagentTools { get; init; } = [];
    public DateTimeOffset? LastActivityEventAtUtc { get; init; }
    public DateTimeOffset? LastUserPromptAtUtc { get; init; }
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
        var subagentTools = reset
            ? new List<ClaudeCodeSubagentToolObservation>()
            : previous!.SubagentTools.ToList();
        var eventName = current.EventName;
        var confirmedDesign = !reset && previous!.HasConfirmedClaudeDesignUsage;
        var isSubagentToolEvent = current.AgentId is not null && current.EventName is
            "PreToolUse" or "PostToolUse" or "PostToolUseFailure";
        if (isSubagentToolEvent && current.AgentId is { } subagentId)
        {
            if (agents.Contains(subagentId) && current.EventName == "PreToolUse" &&
                current.ToolName is { } subagentToolName)
            {
                var toolUseId = current.ToolUseId ?? subagentToolName;
                subagentTools.RemoveAll(tool => tool.AgentId == subagentId && tool.ToolUseId == toolUseId);
                if (subagentTools.Count < 64)
                {
                    subagentTools.Add(new ClaudeCodeSubagentToolObservation(
                        subagentId,
                        toolUseId,
                        ClassifySubagentTool(subagentToolName),
                        current.ObservedAtUtc.ToUniversalTime()));
                }
            }
            else if (agents.Contains(subagentId) && current.EventName is "PostToolUse" or "PostToolUseFailure")
            {
                subagentTools.RemoveAll(tool => tool.AgentId == subagentId &&
                    (current.ToolUseId is { } completedId
                        ? tool.ToolUseId == completedId
                        : current.ToolName is { } completedTool && tool.ToolUseId == completedTool));
            }
        }
        else if (eventName == "PreToolUse" && current.ToolName is { } toolName && tools.Count < 64)
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
            subagentTools.RemoveAll(tool => tool.AgentId == stoppedId);
        }

        // Subagent lifecycle updates party evidence without replacing main-agent activity.
        if (eventName is "SubagentStart" or "SubagentStop" || isSubagentToolEvent)
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
            subagentTools.Clear();
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
            LastUserPromptAtUtc = current.EventName == "UserPromptSubmit" ? current.ObservedAtUtc
                : reset ? null : previous!.LastUserPromptAtUtc,
            SubagentTools = subagentTools.ToArray(),
            HasConfirmedClaudeDesignUsage = confirmedDesign,
            LastActivityEventAtUtc = !reset &&
                (current.EventName is "SubagentStart" or "SubagentStop" || isSubagentToolEvent)
                ? previous!.LastActivityEventAtUtc ?? previous.ObservedAtUtc
                : current.ObservedAtUtc
        };
    }

    private static SubagentWorkKind ClassifySubagentTool(string toolName) => toolName switch
    {
        "Edit" or "Write" or "NotebookEdit" => SubagentWorkKind.Editing,
        "Read" or "Glob" or "Grep" => SubagentWorkKind.Reading,
        "WebSearch" or "WebFetch" => SubagentWorkKind.Researching,
        "Bash" or "PowerShell" => SubagentWorkKind.RunningCommand,
        _ => SubagentWorkKind.Unknown
    };
}
