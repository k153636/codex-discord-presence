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

        if (context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject)
        {
            return WithMcpIdentity(context, BuildIdleActivityLine(context, stateLabel));
        }

        if (context.Codex.ActivityKind == CodexActivityKind.CoordinatingChanges)
        {
            return WithMcpIdentity(context, stateLabel);
        }

        if (context.Codex.ActivityKind == CodexActivityKind.ApplyingEdits)
        {
            return WithMcpIdentity(
                context,
                BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount));
        }

        if (context.Codex.ActivityKind is (CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles) &&
            editedFileCount > 0)
        {
            return WithMcpIdentity(
                context,
                BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount));
        }

        return WithMcpIdentity(context, BuildIdleActivityLine(context, stateLabel));
    }

    private static string WithMcpIdentity(PresenceContext context, string activityLine)
    {
        if (!context.Codex.IsMcpOperation || string.IsNullOrWhiteSpace(activityLine))
        {
            return activityLine;
        }

        var mcpName = McpServerNameFormatter.Format(context.Codex.McpServerName);
        var prefix = string.IsNullOrWhiteSpace(mcpName) ? "MCP" : $"MCP {mcpName}";
        return activityLine.StartsWith("MCP ", StringComparison.Ordinal)
            ? activityLine
            : $"{prefix} {activityLine}";
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
