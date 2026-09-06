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
            return BuildIdleActivityLine(context, stateLabel);
        }

        if (context.Codex.ActivityKind == CodexActivityKind.CoordinatingChanges)
        {
            return stateLabel;
        }

        if (context.Codex.ActivityKind == CodexActivityKind.ApplyingEdits)
        {
            return BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount);
        }

        if (context.Codex.ActivityKind is (CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles) &&
            editedFileCount > 0)
        {
            return BuildEditingActivityLine(context.Codex.ActivityKind, stateLabel, activeFileLabel, editedFileCount);
        }

        return BuildIdleActivityLine(context, stateLabel);
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
