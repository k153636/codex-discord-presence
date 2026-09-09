using System.Globalization;

namespace CodexDiscordPresence;

public sealed class PresenceTemplateRenderer
{
    private static readonly TimeSpan WaitingDetailsRotationInterval = TimeSpan.FromSeconds(5);
    private readonly PresenceStatusLabelResolver _labelResolver = new();
    private readonly Func<DateTime> _utcNow;

    public PresenceTemplateRenderer(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public RenderedPresence Render(PresenceTemplateOptions template, PresenceContext context)
    {
        var values = BuildValues(template, context);

        return new RenderedPresence(
            ResolveDetails(template, context, values),
            Apply(template.State, values),
            RenderLargeImageText(template, values),
            Apply(template.SmallImageText, values),
            template.Buttons.Select(button => new RenderedButton(
                Apply(button.Label, values),
                Apply(button.Url, values))).ToArray(),
            context.Session.StartedAt,
            context.Codex.ActivityKind,
            context.Codex.RunningCommandKind,
            context.Codex.RunningCommandName)
        {
            PartySize = context.Codex.PartySize,
            IsSuccessfulCompletion = context.Codex.IsSuccessfulCompletion,
            IsError = context.Codex.IsError,
            IsThinking = context.Codex.IsThinking,
            WaitingStartedAt = context.Codex.ActivityKind is CodexActivityKind.Ready or CodexActivityKind.WaitingForInput
                ? context.Codex.ActivityStartedAt ?? context.Codex.LastEffectiveSignalAt ?? context.Codex.LastObservedAt
                : null
        };
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
        var billingType = FormatBillingType(context.TokenUsage.BillingType);
        var cost = billingType == "API" && context.TokenUsage.EstimatedCostUsd is not null
            ? FormatCost(context.TokenUsage.EstimatedCostUsd.Value)
            : "";
        var rateLimitDetails = billingType == "subsc"
            ? FormatRateLimitDetails(context.TokenUsage.RateLimit)
            : "";
        var goalModePrefix = FormatGoalModePrefix(context);
        var stateLabel = _labelResolver.ResolveStateLabel(template, context, context.Codex.ActivityKind, activityFileCount);
        if (context.Codex.ActivityKind == CodexActivityKind.AnalyzingProject &&
            context.Codex.ActivityRepeatCount > 1 &&
            string.IsNullOrWhiteSpace(context.Codex.LatestThinkingSummary))
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
            ["ThinkingSummary"] = ThinkingSummaryFormatter.FormatForCurrentReasoning(
                context.Codex.LatestThinkingSummary,
                context.Codex.LatestActivityEventKind) ?? "",
            ["RunningCommandName"] = ResolveRunningCommandName(context.Codex.RunningCommandName, context.Codex.RunningCommandKind),
            ["RunningCommandKind"] = context.Codex.RunningCommandKind.ToString(),
            ["ProjectFileCount"] = context.Project.TotalFileCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectLineCount"] = context.Project.TotalLineCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectSizeText"] = projectSizeText,
            ["SessionElapsed"] = FormatElapsed(context.Session.Elapsed),
            ["SessionStartedAt"] = context.Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["Tokens"] = context.TokenUsage.TotalTokens is null
                ? ""
                : $"{CompactNumberFormatter.Format(context.TokenUsage.TotalTokens.Value)} Token",
            ["Cost"] = cost,
            ["BillingType"] = billingType,
            ["RateLimitDetails"] = rateLimitDetails,
            ["EstimatedCost"] = "",
            ["CodexState"] = stateLabel,
        };

        return values;
    }

    private string ResolveDetails(
        PresenceTemplateOptions template,
        PresenceContext context,
        IReadOnlyDictionary<string, string> values)
    {
        var defaultDetails = Apply(template.Details, values);
        if (context.Codex.ActivityKind != CodexActivityKind.Ready ||
            (string.IsNullOrWhiteSpace(values["Cost"]) && string.IsNullOrWhiteSpace(values["BillingType"])))
        {
            return defaultDetails;
        }

        var waitingStartedAt = context.Codex.ActivityStartedAt ??
            context.Codex.LastEffectiveSignalAt ??
            context.Codex.LastObservedAt;
        if (!waitingStartedAt.HasValue)
        {
            return defaultDetails;
        }

        var elapsed = _utcNow() - waitingStartedAt.Value;
        if (elapsed < WaitingDetailsRotationInterval)
        {
            return defaultDetails;
        }

        var phase = (long)(elapsed.Ticks / WaitingDetailsRotationInterval.Ticks);
        return phase % 2 == 0
            ? defaultDetails
            : Apply(template.WaitingDetails, values);
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
            var placeholder = "{" + pair.Key + "}";
            if (string.IsNullOrWhiteSpace(pair.Value))
            {
                rendered = rendered
                    .Replace(" • " + placeholder, "", StringComparison.OrdinalIgnoreCase)
                    .Replace(placeholder + " • ", "", StringComparison.OrdinalIgnoreCase)
                    .Replace(placeholder, "", StringComparison.OrdinalIgnoreCase);
                continue;
            }

            rendered = rendered.Replace(placeholder, pair.Value, StringComparison.OrdinalIgnoreCase);
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

    private static string FormatCost(decimal value)
    {
        var format = value >= 1m ? "0.00" : "0.0000";
        return "$" + value.ToString(format, CultureInfo.InvariantCulture);
    }

    private string FormatRateLimitDetails(RateLimitSnapshot? rateLimit)
    {
        if (rateLimit is null || rateLimit.WindowDurationMinutes != 300)
        {
            return "";
        }

        var remaining = rateLimit.ResetAtUtc - _utcNow();
        var resetMinutes = remaining <= TimeSpan.Zero
            ? 0L
            : (long)Math.Ceiling(remaining.TotalMinutes);
        var resetHours = resetMinutes / 60;
        var remainingMinutes = resetMinutes % 60;
        var resetText = $"{resetHours}h {remainingMinutes}m";

        return $" \u2022 5h {rateLimit.UsedPercent.ToString(CultureInfo.InvariantCulture)}% used \u2022 reset {resetText}";
    }

    private static string FormatBillingType(string? billingType)
    {
        return billingType?.Trim().ToLowerInvariant() switch
        {
            "api" or "apikey" => "API",
            "subsc" or "subscription" or "chatgpt" or "free" or "go" or "plus" or "pro" or "business" or "enterprise" or "edu" => "subsc",
            _ => string.Empty
        };
    }

    private static string FormatChangedFiles(int count)
    {
        return count == 1 ? "1 file changed" : $"{count.ToString(CultureInfo.InvariantCulture)} files changed";
    }

    private static string FormatProjectSize(int fileCount, long lineCount)
    {
        var files = fileCount == 1 ? "1 file" : $"{CompactNumberFormatter.Format(fileCount)} files";
        var lines = lineCount == 1 ? "1 line" : $"{CompactNumberFormatter.Format(lineCount)} lines";
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
    string RunningCommandName)
{
    public int? PartySize { get; init; }
    public bool IsSuccessfulCompletion { get; init; }
    public bool IsError { get; init; }
    public bool IsThinking { get; init; }
    public DateTime? WaitingStartedAt { get; init; }
}

public sealed record RenderedButton(string Label, string Url);


