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
        var hasEditedFile = false;
        if (context.Project.RecentFilePath != null)
        {
            try
            {
                var fileInfo = new FileInfo(context.Project.RecentFilePath);
                if (fileInfo.Exists)
                {
                    var sessionStart = context.Session.StartedAt;
                    if (fileInfo.LastWriteTimeUtc >= sessionStart.AddMinutes(-1))
                    {
                        hasEditedFile = true;
                    }
                }
            }
            catch
            {
                // Ignore
            }
        }

        var editingFileName = hasEditedFile ? context.Project.RecentFileName ?? "" : "";
        var codexState = !context.Codex.IsRunning ? template.OfflineText
            : context.Codex.IsThinking ? template.ThinkingText
            : template.WaitingText;
        var changedFilesText = FormatChangedFiles(context.Git.ChangedFileCount);
        var projectSizeText = FormatProjectSize(context.Project.ScannedFileCount, context.Project.TotalLineCount);
        var activityLine = !string.IsNullOrWhiteSpace(editingFileName)
            ? $"Editing {editingFileName} ・ {changedFilesText}"
            : BuildIdleActivityLine(template, context, codexState, changedFilesText);

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
            ["ChangedFileCount"] = context.Git.ChangedFileCount.ToString(CultureInfo.InvariantCulture),
            ["ChangedFilesText"] = changedFilesText,
            ["ActivityLine"] = activityLine,
            ["ProjectFileCount"] = context.Project.ScannedFileCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectLineCount"] = context.Project.TotalLineCount.ToString(CultureInfo.InvariantCulture),
            ["ProjectSizeText"] = projectSizeText,
            ["SessionElapsed"] = FormatElapsed(context.Session.Elapsed),
            ["SessionStartedAt"] = context.Session.StartedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
            ["Tokens"] = context.TokenUsage.TotalTokens is null ? "tokens pending" : FormatNumber(context.TokenUsage.TotalTokens.Value),
            ["EstimatedCost"] = context.TokenUsage.EstimatedCostUsd is null ? "cost pending" : "$" + context.TokenUsage.EstimatedCostUsd.Value.ToString("0.00", CultureInfo.InvariantCulture),
            ["CodexState"] = codexState
        };
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

    private static string BuildIdleActivityLine(
        PresenceTemplateOptions template,
        PresenceContext context,
        string codexState,
        string changedFilesText)
    {
        if (!context.Codex.IsRunning)
        {
            return $"{codexState} ・ {changedFilesText}";
        }

        if (context.Codex.IsThinking)
        {
            return $"{codexState} on {context.Project.Name} ・ {changedFilesText}";
        }

        return $"{template.WaitingActivityText} ・ {changedFilesText}";
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
