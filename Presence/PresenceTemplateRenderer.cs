using System.Globalization;

namespace CodexDiscordPresence;

public sealed class PresenceTemplateRenderer
{
    private readonly PresenceStatusLabelResolver _labelResolver = new();

    public RenderedPresence Render(PresenceTemplateOptions template, PresenceContext context)
    {
        var values = BuildValues(template, context);

        return new RenderedPresence(
            Apply(template.Details, values),
            Apply(template.State, values),
            RenderLargeImageText(template, values),
            Apply(template.SmallImageText, values),
            template.Buttons.Select(button => new RenderedButton(
                Apply(button.Label, values),
                Apply(button.Url, values))).ToArray(),
            context.Session.StartedAt,
            context.Codex.ActivityKind,
            context.Codex.RunningCommandKind,
            context.Codex.RunningCommandName);
    }

    private Dictionary<string, string> BuildValues(PresenceTemplateOptions template, PresenceContext context)
    {
        var editingFileSelection = EditedFileSelector.Select(context, template.EditingFreshnessSeconds);
        var editingFile = editingFileSelection.ActiveFile;
        var editingFileName = editingFile is null
            ? ""
            : EditedFileSelector.FormatForDisplay(context.Project, editingFile);
        var isFileMutationActivity = context.Codex.ActivityKind is
            (CodexActivityKind.ApplyingEdits or
             CodexActivityKind.CoordinatingChanges or
             CodexActivityKind.CreatingFiles or
             CodexActivityKind.DeletingFiles);
        var activityFileCount = !isFileMutationActivity
            ? editingFileSelection.TotalFileCount
            : context.Codex.ActivityKind == CodexActivityKind.CoordinatingChanges && context.Codex.ActivityFilePaths.Count == 0
                ? Math.Max(editingFileSelection.TotalFileCount, context.Git.ChangedFileCount)
                : Math.Max(editingFileSelection.TotalFileCount, context.Codex.ActivityFilePaths.Count);
        var editingFileLabel = BuildEditingFileLabel(context, editingFileName, activityFileCount);
        var changedFilesText = FormatChangedFiles(context.Git.ChangedFileCount);
        var projectSizeText = FormatProjectSize(context.Project.TotalFileCount, context.Project.TotalLineCount);
        var goalModePrefix = FormatGoalModePrefix(context);
        var stateLabel = _labelResolver.ResolveStateLabel(template, context, context.Codex.ActivityKind, activityFileCount);
        if (context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            context.Codex.ActivityRepeatCount > 1)
        {
            stateLabel = $"{stateLabel} x{context.Codex.ActivityRepeatCount}";
        }
        var activityLine = PresenceActivityComposer.BuildActivityLine(context, stateLabel, editingFileName, activityFileCount);

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
            ["EditingFilePath"] = editingFileName,
            ["ActiveEditedFileCount"] = activityFileCount.ToString(CultureInfo.InvariantCulture),
            ["ActiveEditedFilesText"] = BuildActiveEditedFilesText(context, stateLabel, editingFileName, activityFileCount),
            ["ChangedFileCount"] = context.Git.ChangedFileCount.ToString(CultureInfo.InvariantCulture),
            ["ChangedFilesText"] = changedFilesText,
            ["ActivityLabel"] = stateLabel,
            ["ActivityKind"] = context.Codex.ActivityKind.ToString(),
            ["ActivityConfidence"] = context.Codex.Confidence.ToString(),
            ["ActivityProvenance"] = context.Codex.ActivityProvenance.ToString(),
            ["ActivityReason"] = context.Codex.ActivityReason,
            ["ActivityLine"] = activityLine,
            ["RunningCommandName"] = ResolveRunningCommandName(context.Codex.RunningCommandName, context.Codex.RunningCommandKind),
            ["RunningCommandKind"] = context.Codex.RunningCommandKind.ToString(),
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

    private string BuildActiveEditedFilesText(
        PresenceContext context,
        string stateLabel,
        string editingFileName,
        int activityFileCount)
    {
        return PresenceActivityComposer.BuildActivityLine(context, stateLabel, editingFileName, activityFileCount);
    }

    private static string BuildEditingFileLabel(
        PresenceContext context,
        string editingFileName,
        int activityFileCount)
    {
        if (string.IsNullOrWhiteSpace(editingFileName))
        {
            return "";
        }

        if (context.Codex.ActivityKind is not (CodexActivityKind.ApplyingEdits or CodexActivityKind.CoordinatingChanges or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles))
        {
            return "";
        }

        var label = $"Editing {editingFileName}";
        if (context.Codex.ActivityKind == CodexActivityKind.ApplyingEdits && activityFileCount >= 4)
        {
            label += $" + {activityFileCount - 1} files";
        }

        return label;
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

    private static string? RenderLargeImageText(PresenceTemplateOptions template, IReadOnlyDictionary<string, string> values)
    {
        if (!template.EnableLargeImageText)
        {
            return null;
        }

        var rendered = Apply(template.LargeImageText, values);
        return string.IsNullOrWhiteSpace(rendered) ? null : rendered;
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

    private static string ResolveRunningCommandName(string commandName, RunningCommandKind commandKind)
    {
        if (!string.IsNullOrWhiteSpace(commandName))
        {
            return commandName;
        }

        return "";
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
    string? LargeImageText,
    string SmallImageText,
    IReadOnlyList<RenderedButton> Buttons,
    DateTime? StartedAt,
    CodexActivityKind ActivityKind,
    RunningCommandKind RunningCommandKind,
    string RunningCommandName);

public sealed record RenderedButton(string Label, string Url);


