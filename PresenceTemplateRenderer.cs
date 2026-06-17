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
        var recentEditedFiles = context.Project.RecentFiles
            .Where(file => DateTime.UtcNow - file.LastWriteTimeUtc <= TimeSpan.FromSeconds(Math.Max(5, template.EditingFreshnessSeconds)))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
        var editingFileName = recentEditedFiles.FirstOrDefault()?.Name ?? "";
        var changedFilesText = FormatChangedFiles(context.Git.ChangedFileCount);
        var projectSizeText = FormatProjectSize(context.Project.ScannedFileCount, context.Project.TotalLineCount);
        var stateLabel = ResolveStateLabel(template, context.Codex.ActivityKind, context.Git.ChangedFileCount);
        var activityLine = BuildActivityLine(context, recentEditedFiles, stateLabel);

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModelName"] = context.ModelName,
            ["CodexStatus"] = context.Codex.IsRunning ? "Codex running" : "Codex not detected",
            ["CodexProcessName"] = context.Codex.ProcessName ?? "",
            ["ProjectName"] = context.Project.Name,
            ["ProjectPath"] = context.Project.Path,
            ["EditingFileName"] = editingFileName,
            ["EditingFileLabel"] = string.IsNullOrWhiteSpace(editingFileName) ? "" : $"Editing {editingFileName}",
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
            ["ProjectFileCount"] = context.Project.ScannedFileCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectLineCount"] = context.Project.TotalLineCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectSizeText"] = projectSizeText,
            ["SessionElapsed"] = FormatElapsed(context.Session.Elapsed),
            ["SessionStartedAt"] = context.Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["Tokens"] = context.TokenUsage.TotalTokens is null ? "tokens pending" : FormatNumber(context.TokenUsage.TotalTokens.Value),
            ["EstimatedCost"] = context.TokenUsage.EstimatedCostUsd is null ? "cost pending" : "$" + context.TokenUsage.EstimatedCostUsd.Value.ToString("0.00", CultureInfo.InvariantCulture),
            ["CodexState"] = stateLabel
        };
    }

    private static string BuildActivityLine(
        PresenceContext context,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        string stateLabel)
    {
        if (!context.Codex.IsRunning)
        {
            return stateLabel;
        }

        if (context.Codex.ActivityKind == CodexActivityKind.RunningCommand)
        {
            return stateLabel;
        }

        if (recentEditedFiles.Count > 0)
        {
            return BuildEditingActivityLine(stateLabel, recentEditedFiles);
        }

        return BuildIdleActivityLine(context, stateLabel);
    }

    private static string BuildActiveEditedFilesText(
        PresenceTemplateOptions template,
        PresenceContext context,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles)
    {
        if (recentEditedFiles.Count == 0)
        {
            return "";
        }

        var stateLabel = ResolveStateLabel(template, context.Codex.ActivityKind, context.Git.ChangedFileCount);
        return BuildEditingActivityLine(stateLabel, recentEditedFiles);
    }

    private static string BuildEditingActivityLine(
        string stateLabel,
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles)
    {
        if (recentEditedFiles.Count == 1)
        {
            return $"{stateLabel} ・ Editing {recentEditedFiles[0].Name}";
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
            CodexActivityKind.UpdatingFiles => stateLabel,
            CodexActivityKind.Refactoring => stateLabel,
            CodexActivityKind.AnalyzingProject => stateLabel,
            CodexActivityKind.RunningCommand => stateLabel,
            _ => stateLabel
        };
    }

    private static string ResolveStateLabel(PresenceTemplateOptions template, CodexActivityKind activityKind, int changedFileCount)
    {
        var label = activityKind switch
        {
            CodexActivityKind.Planning => FirstNonEmpty(template.PlanningText, "Planning"),
            CodexActivityKind.ApplyingEdits => FirstNonEmpty(template.ApplyingEditsText, "Applying edits"),
            CodexActivityKind.UpdatingFiles => FirstNonEmpty(template.UpdatingFilesText, "Updating files"),
            CodexActivityKind.RunningCommand => FirstNonEmpty(template.RunningCommandText, "Running command"),
            CodexActivityKind.Refactoring => FirstNonEmpty(template.RefactoringText, "Refactoring"),
            CodexActivityKind.AnalyzingProject => FirstNonEmpty(template.ThinkingText, template.AnalyzingProjectText, template.AnalyzingText, "Thinking"),
            CodexActivityKind.Ready => FirstNonEmpty(template.IdlingText, template.ReadyText, "Idling"),
            CodexActivityKind.Offline => FirstNonEmpty(template.OfflineText, template.IdlingText, "Idling"),
            _ => FirstNonEmpty(template.IdlingText, template.ReadyText, "Idling")
        };

        return label.Replace("{n}", changedFileCount.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
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
            return (value / 1_000_000D).ToString("0.#M", CultureInfo.InvariantCulture);
        }

        if (value >= 1_000)
        {
            return (value / 1_000D).ToString("0.#k", CultureInfo.InvariantCulture);
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatChangedFiles(int count)
    {
        return count == 1 ? "1 file changed" : $"{count.ToString(CultureInfo.InvariantCulture)} files changed";
    }

    private static string FormatProjectSize(int fileCount, long lineCount)
    {
        var files = fileCount == 1 ? "1 file" : $"{FormatNumber(fileCount)} files";
        var lines = lineCount == 1 ? "1 line" : $"{FormatNumber(lineCount)} lines";
        return $"{files} ・ {lines}";
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
