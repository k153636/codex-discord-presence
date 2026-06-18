using System.Globalization;

namespace CodexDiscordPresence;

public sealed class PresenceTemplateRenderer
{
    public RenderedPresence Render(PresenceTemplateOptions template, PresenceContext context)
    {
        var values = BuildValues(template, context);

        return new RenderedPresence(
            Apply(template.Details, values),
            Apply(template.State, values),
            Apply(template.LargeImageText, values),
            Apply(template.SmallImageText, values),
            template.Buttons.Select(button => new RenderedButton(
                Apply(button.Label, values),
                Apply(button.Url, values))).ToArray(),
            context.Session.StartedAt);
    }

    private static Dictionary<string, string> BuildValues(PresenceTemplateOptions template, PresenceContext context)
    {
        var recentEditedFiles = context.Codex.RecentEditedFiles
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();

        var editingFile = SelectEditingFile(recentEditedFiles);
        var editingFileName = editingFile?.Name ?? "";
        var editingFileLabel = BuildEditingFileLabel(context, editingFileName);
        var changedFilesText = FormatChangedFiles(context.Git.ChangedFileCount);
        var projectSizeText = FormatProjectSize(context.Project.TotalFileCount, context.Project.TotalLineCount);
        var goalModePrefix = FormatGoalModePrefix(context);
        var stateLabel = ResolveStateLabel(template, context, context.Codex.ActivityKind, recentEditedFiles.Length);
        if (context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            context.Codex.ActivityRepeatCount > 1)
        {
            stateLabel = $"{stateLabel} x{context.Codex.ActivityRepeatCount}";
        }
        var activityLine = PresenceActivityComposer.BuildActivityLine(context, recentEditedFiles, stateLabel, editingFile);

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModelName"] = context.ModelName,
            ["CodexStatus"] = context.Codex.IsRunning ? "Codex running" : "Codex not detected",
            ["CodexProcessName"] = context.Codex.ProcessName ?? "",
            ["ProjectName"] = context.Project.Name,
            ["ProjectPath"] = context.Project.Path,
            ["GoalModePrefix"] = goalModePrefix,
            ["EditingFileName"] = editingFileName,
            ["EditingFileLabel"] = editingFileLabel,
            ["EditingFilePath"] = context.Project.RecentFilePath ?? "",
            ["ActiveEditedFileCount"] = recentEditedFiles.Length.ToString(CultureInfo.InvariantCulture),
            ["ActiveEditedFilesText"] = BuildActiveEditedFilesText(template, context, recentEditedFiles),
            ["ChangedFileCount"] = context.Git.ChangedFileCount.ToString(CultureInfo.InvariantCulture),
            ["ChangedFilesText"] = changedFilesText,
            ["ActivityLabel"] = stateLabel,
            ["ActivityKind"] = context.Codex.ActivityKind.ToString(),
            ["ActivityConfidence"] = context.Codex.Confidence.ToString(),
            ["ActivityProvenance"] = context.Codex.ActivityProvenance.ToString(),
            ["ActivityReason"] = context.Codex.ActivityReason,
            ["ActivityLine"] = activityLine,
            ["ProjectFileCount"] = context.Project.TotalFileCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectLineCount"] = context.Project.TotalLineCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectSizeText"] = projectSizeText,
            ["SessionElapsed"] = FormatElapsed(context.Session.Elapsed),
            ["SessionStartedAt"] = context.Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["Tokens"] = context.TokenUsage.TotalTokens is null ? "Tokens pending" : $"{FormatNumber(context.TokenUsage.TotalTokens.Value)} Token",
            ["Cost"] = "",
            ["EstimatedCost"] = "",
            ["CodexState"] = stateLabel,
        };

        return values;
    }

    private static string BuildActiveEditedFilesText(
        PresenceTemplateOptions template,
        PresenceContext context,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles)
    {
        if (recentEditedFiles.Count != 1)
        {
            return "";
        }

        var stateLabel = ResolveStateLabel(template, context, context.Codex.ActivityKind, recentEditedFiles.Count);
        var editingFile = SelectEditingFile(recentEditedFiles);
        if (editingFile is null)
        {
            return "";
        }

        return $"{stateLabel} \u2022 {editingFile.Name}";
    }

    private static string BuildEditingFileLabel(
        PresenceContext context,
        string editingFileName)
    {
        if (string.IsNullOrWhiteSpace(editingFileName))
        {
            return "";
        }

        if (context.Codex.ActivityKind is not (CodexActivityKind.ApplyingEdits or CodexActivityKind.CoordinatingChanges or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles))
        {
            return "";
        }

        return $"Editing {editingFileName}";
    }

    private static string ResolveStateLabel(
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
            CodexActivityKind.RunningCommand => FirstNonEmpty(template.RunningCommandText, "Running command"),
            CodexActivityKind.Refactoring => FirstNonEmpty(template.RefactoringText, "Refactoring"),
            CodexActivityKind.AnalyzingProject => ShouldUseWorkingLabel(context)
                ? FirstNonEmpty(template.WorkingText, template.AnalyzingProjectText, template.AnalyzingText, template.ThinkingText, "Analyzing project")
                : FirstNonEmpty(template.AnalyzingProjectText, template.AnalyzingText, template.ThinkingText, "Analyzing project"),
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
            return FirstNonEmpty(template.ReadyText, "Standing by");
        }

        return FirstNonEmpty(template.IdlingText, "Idling");
    }

    private static bool ShouldUseWorkingLabel(PresenceContext context)
    {
        return context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            context.Codex.LastTaskStartedAt.HasValue;
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

    private static string Apply(string value, IReadOnlyDictionary<string, string> values)
    {
        var rendered = value;
        foreach (var pair in values)
        {
            rendered = rendered.Replace("{" + pair.Key + "}", pair.Value, StringComparison.OrdinalIgnoreCase);
        }

        return string.IsNullOrWhiteSpace(rendered) ? "" : rendered.Trim();
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalHours >= 1)
        {
            return $"{(int)elapsed.TotalHours}h {elapsed.Minutes}m";
        }

        return $"{Math.Max(1, elapsed.Minutes)}m";
    }

    private static string FormatNumber(long value)
    {
        if (value >= 1_000_000)
        {
            return $"{(value / 1_000_000D).ToString("0.#", CultureInfo.InvariantCulture)}M";
        }

        if (value >= 1_000)
        {
            return $"{(value / 1_000D).ToString("0.#", CultureInfo.InvariantCulture)}K";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static RecentProjectFileSnapshot? SelectEditingFile(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles)
    {
        if (recentEditedFiles.Count == 0)
        {
            return null;
        }

        return recentEditedFiles[0];
    }

    private static string FormatCost(decimal value)
    {
        var format = value >= 1m ? "0.00" : "0.0000";
        return "$" + value.ToString(format, CultureInfo.InvariantCulture);
    }

    private static string FormatChangedFiles(int count)
    {
        return count == 1 ? "1 file changed" : $"{count.ToString(CultureInfo.InvariantCulture)} files changed";
    }

    private static string FormatProjectSize(int fileCount, long lineCount)
    {
        var files = fileCount == 1 ? "1 file" : $"{FormatNumber(fileCount)} files";
        var lines = lineCount == 1 ? "1 line" : $"{FormatNumber(lineCount)} lines";
        return $"{files} \u2022 {lines}";
    }

    private static string FormatGoalModePrefix(PresenceContext context)
    {
        var collaborationMode = context.Codex.CollaborationMode;
        if (string.IsNullOrWhiteSpace(collaborationMode))
        {
            return "";
        }

        return collaborationMode.Trim().ToLowerInvariant() switch
        {
            "plan" when IsImplementationActivity(context.Codex.ActivityKind) => "Code mode:",
            "plan" => "Plan mode:",
            "goal" when IsImplementationActivity(context.Codex.ActivityKind) => "Code mode:",
            "goal" => "Plan mode:",
            _ => ""
        };
    }

    private static bool IsImplementationActivity(CodexActivityKind activityKind)
    {
        return activityKind is CodexActivityKind.ApplyingEdits
            or CodexActivityKind.CoordinatingChanges
            or CodexActivityKind.CreatingFiles
            or CodexActivityKind.DeletingFiles
            or CodexActivityKind.RunningCommand
            or CodexActivityKind.Refactoring;
    }
}

public sealed record RenderedPresence(
    string Details,
    string State,
    string LargeImageText,
    string SmallImageText,
    IReadOnlyList<RenderedButton> Buttons,
    DateTime? StartedAt);

public sealed record RenderedButton(string Label, string Url);


