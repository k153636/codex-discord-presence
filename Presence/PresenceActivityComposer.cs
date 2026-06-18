using System.Globalization;

namespace CodexDiscordPresence;

internal static class PresenceActivityComposer
{
    public static string BuildActivityLine(
        PresenceContext context,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        string stateLabel,
        RecentProjectFileSnapshot? editingFile)
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

        if (context.Codex.ActivityKind is CodexActivityKind.ApplyingEdits or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles &&
            recentEditedFiles.Count > 0)
        {
            return BuildEditingActivityLine(stateLabel, editingFile);
        }

        return BuildIdleActivityLine(context, stateLabel);
    }

    private static string BuildEditingActivityLine(
        string stateLabel,
        RecentProjectFileSnapshot? editingFile)
    {
        if (editingFile is not null)
        {
            return $"{stateLabel} • {editingFile.Name}";
        }

        return stateLabel;
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
