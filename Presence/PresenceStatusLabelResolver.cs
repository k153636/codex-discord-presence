using System.Globalization;

namespace CodexDiscordPresence;

public sealed class PresenceStatusLabelResolver
{
    public string ResolveStateLabel(
        PresenceTemplateOptions template,
        PresenceContext context,
        CodexActivityKind activityKind,
        int changedFileCount)
    {
        var label = activityKind switch
        {
            CodexActivityKind.Planning => FirstNonEmpty(template.PlanningText, "Planning"),
            CodexActivityKind.ApplyingEdits => FirstNonEmpty(template.ApplyingEditsText, "Applying edits"),
            CodexActivityKind.CoordinatingChanges => FirstNonEmpty(template.CoordinatingChangesText, "Coordinating changes across {n} files"),
            CodexActivityKind.CreatingFiles => FirstNonEmpty(template.CreatingFilesText, "Creating files"),
            CodexActivityKind.DeletingFiles => FirstNonEmpty(template.DeletingFilesText, "Deleting files"),
            CodexActivityKind.RunningCommand => ResolveRunningCommandLabel(template, context),
            CodexActivityKind.Refactoring => FirstNonEmpty(template.RefactoringText, "Refactoring"),
            CodexActivityKind.AnalyzingProject => ShouldUseWorkingLabel(context)
                ? FirstNonEmpty(template.WorkingText, template.InvestigatingText, template.AnalyzingProjectText, template.AnalyzingText, template.ThinkingText, "Analyzing project")
                : FirstNonEmpty(template.InvestigatingText, template.WorkingText, template.AnalyzingProjectText, template.AnalyzingText, template.ThinkingText, "Investigating"),
            CodexActivityKind.Ready => ResolveReadyLabel(template, context),
            CodexActivityKind.Offline => FirstNonEmpty(template.OfflineText, template.IdlingText, "Idling"),
            _ => FirstNonEmpty(template.IdlingText, template.ReadyText, "Idling")
        };

        return label.Replace("{n}", changedFileCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    private static string ResolveReadyLabel(PresenceTemplateOptions template, PresenceContext context)
    {
        var lastObservedAt = context.Codex.LastObservedAt ?? context.Session.StartedAt;
        var idleGrace = TimeSpan.FromMinutes(Math.Max(0, template.ReadyIdleGraceMinutes));
        var elapsedSinceLastObserved = DateTime.UtcNow - lastObservedAt;

        if (elapsedSinceLastObserved < idleGrace)
        {
            return FirstNonEmpty(template.WaitingText, template.ReadyText, "Waiting");
        }

        return FirstNonEmpty(template.IdlingText, "Idling");
    }

    private static string ResolveRunningCommandLabel(PresenceTemplateOptions template, PresenceContext context)
    {
        var baseLabel = FirstNonEmpty(template.RunningCommandText, "Run Command");
        var commandName = ResolveRunningCommandName(context.Codex.RunningCommandName, context.Codex.RunningCommandKind);

        if (string.IsNullOrWhiteSpace(commandName))
        {
            return StripRunningCommandPlaceholder(baseLabel);
        }

        if (template.RunningCommandText.Contains("{RunningCommandName}", StringComparison.OrdinalIgnoreCase))
        {
            return baseLabel;
        }

        return $"{baseLabel}: {commandName}";
    }

    private static string ResolveRunningCommandName(string commandName, RunningCommandKind commandKind)
    {
        if (!string.IsNullOrWhiteSpace(commandName))
        {
            return commandName;
        }

        return commandKind switch
        {
            RunningCommandKind.Git => "git",
            RunningCommandKind.Search => "search",
            RunningCommandKind.Build => "build",
            RunningCommandKind.Test => "test",
            _ => ""
        };
    }

    private static string StripRunningCommandPlaceholder(string value)
    {
        return value
            .Replace(": {RunningCommandName}", "", StringComparison.OrdinalIgnoreCase)
            .Replace("{RunningCommandName}", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
    }

    private static bool ShouldUseWorkingLabel(PresenceContext context)
    {
        return context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            !ShouldUseInvestigatingLabel(context) &&
            context.Codex.LastTaskStartedAt.HasValue;
    }

    private static bool ShouldUseInvestigatingLabel(PresenceContext context)
    {
        return context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            context.Codex.LastShellCommandWasInvestigative;
    }

    private static string FirstNonEmpty(params string[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }
}
