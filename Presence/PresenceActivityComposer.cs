namespace CodexDiscordPresence;

internal static class PresenceActivityComposer
{
    public static string BuildActivityLine(
        PresenceContext context,
        string stateLabel,
        string activeFileLabel,
        int editedFileCount)
    {
        if (!context.Codex.IsRunning)
        {
            return stateLabel;
        }

        var thinkingSummary = ThinkingSummaryFormatter.FormatForPresence(context.Codex.LatestThinkingSummary);
        if (ShouldDisplayThinkingSummary(context, thinkingSummary))
        {
            return MainAgentActivityComposer.AddRole(context, thinkingSummary!);
        }

        if (ShouldDisplayMcpIdentity(context, thinkingSummary))
        {
            return BuildMcpActivityLine(context);
        }

        if (context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject)
        {
            return WithMainAgentRole(context, BuildIdleActivityLine(context, stateLabel));
        }

        if (context.Codex.ActivityKind == CodexActivityKind.CoordinatingChanges)
        {
            return WithMainAgentRole(context, stateLabel);
        }

        if (context.Codex.ActivityKind == CodexActivityKind.ApplyingEdits)
        {
            return WithMainAgentRole(
                context,
                BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount));
        }

        if (context.Codex.ActivityKind is (CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles) &&
            editedFileCount > 0)
        {
            return WithMainAgentRole(
                context,
                BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount));
        }

        return WithMainAgentRole(context, BuildIdleActivityLine(context, stateLabel));
    }

    private static string WithMainAgentRole(PresenceContext context, string activityLine)
    {
        return MainAgentActivityComposer.AddRole(context, activityLine);
    }

    private static bool ShouldDisplayThinkingSummary(
        PresenceContext context,
        string? thinkingSummary)
    {
        if (string.IsNullOrWhiteSpace(thinkingSummary))
        {
            return false;
        }

        if (context.Codex.IsMcpOperation && context.Codex.LatestActivityEventKind is null)
        {
            return false;
        }

        return context.Codex.LatestActivityEventKind is null or CodexActivityEventKind.Reasoning;
    }

    private static bool ShouldDisplayMcpIdentity(
        PresenceContext context,
        string? thinkingSummary)
    {
        if (!context.Codex.IsMcpOperation || !context.Codex.ActivityKind.IsActive())
        {
            return false;
        }

        return context.Codex.LatestActivityEventKind is not CodexActivityEventKind.Reasoning ||
            string.IsNullOrWhiteSpace(thinkingSummary);
    }

    private static string BuildMcpActivityLine(PresenceContext context)
    {
        var mcpName = McpServerNameFormatter.Format(
            context.Codex.McpServerName ?? context.Codex.ActiveMcpServerNames.FirstOrDefault());
        var activeMcpCount = context.Codex.ActiveMcpServerNames.Count;
        if (activeMcpCount == 0 && !string.IsNullOrWhiteSpace(mcpName))
        {
            activeMcpCount = 1;
        }

        if (string.IsNullOrWhiteSpace(mcpName))
        {
            return activeMcpCount > 1
                ? $"MCP＆+{activeMcpCount - 1}"
                : "MCP";
        }

        return activeMcpCount > 1
            ? $"MCP {mcpName}＆+{activeMcpCount - 1}"
            : $"MCP {mcpName}";
    }

    private static string BuildEditingActivityLine(
        CodexActivityKind activityKind,
        string stateLabel,
        string activeFileLabel,
        int editedFileCount)
    {
        if (string.IsNullOrWhiteSpace(activeFileLabel))
        {
            return activityKind == CodexActivityKind.ApplyingEdits ? "Editing" : stateLabel;
        }

        var prefix = activityKind == CodexActivityKind.ApplyingEdits ? "Editing" : stateLabel;
        var activityLine = $"{prefix} {activeFileLabel}";
        if (activityKind == CodexActivityKind.ApplyingEdits && editedFileCount >= 4)
        {
            activityLine += $" + {editedFileCount - 1} files";
        }

        return activityLine;
    }

    private static string BuildIdleActivityLine(
        PresenceContext context,
        string stateLabel)
    {
        return context.Codex.ActivityKind switch
        {
            CodexActivityKind.Planning => stateLabel,
            CodexActivityKind.ApplyingEdits => stateLabel,
            CodexActivityKind.CreatingFiles => stateLabel,
            CodexActivityKind.DeletingFiles => stateLabel,
            CodexActivityKind.Refactoring => stateLabel,
            CodexActivityKind.AnalyzingProject => stateLabel,
            CodexActivityKind.RunningCommand => stateLabel,
            _ => stateLabel
        };
    }
}
