using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

public sealed class CodexModelNameProvider
{
    private readonly CodexDetectionOptions _codexOptions;
    private readonly PresenceTemplateOptions _presenceOptions;
    private readonly string _codexHomePath;

    public CodexModelNameProvider(CodexDetectionOptions codexOptions, PresenceTemplateOptions presenceOptions)
    {
        _codexOptions = codexOptions;
        _presenceOptions = presenceOptions;
        _codexHomePath = codexOptions.GetResolvedHomePath();
    }

    public string GetModelName(string projectPath)
    {
        return GetSnapshot(projectPath).DisplayLabel;
    }

    public ModelNameSnapshot GetSnapshot(string projectPath)
    {
        var fallback = FallbackModelName();
        var environmentModel = DetectFromEnvironment();
        var selectedUiModel = DetectFromConfig();
        var selectedUiReasoningEffort = DetectReasoningEffortFromConfig();
        var selectedUiServiceTier = DetectServiceTierFromConfig();
        var selectedUiModelTime = GetConfigLastWriteTimeUtc();
        var sessionModel = DetectFromRecentSessions(projectPath);
        var currentProjectSession = sessionModel is { IsProjectMatch: true } &&
            sessionModel.LastActivityAt >= selectedUiModelTime
            ? sessionModel
            : null;

        if (!_presenceOptions.AutoDetectModelName)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel?.ModelName, fallback, "fallback");
        }

        if (environmentModel != null)
        {
            var environmentSettings = currentProjectSession is not null &&
                string.Equals(currentProjectSession.ModelName, environmentModel, StringComparison.OrdinalIgnoreCase)
                ? currentProjectSession
                : null;
            return new ModelNameSnapshot(
                selectedUiModel,
                sessionModel?.ModelName,
                environmentModel,
                "environment",
                environmentSettings?.ReasoningEffort ?? selectedUiReasoningEffort,
                environmentSettings?.ServiceTier ?? selectedUiServiceTier);
        }

        if (currentProjectSession?.ModelName != null)
        {
            var projectSessionModel = currentProjectSession.ModelName;
            return new ModelNameSnapshot(
                selectedUiModel,
                projectSessionModel,
                projectSessionModel,
                "project-session",
                currentProjectSession.ReasoningEffort ?? selectedUiReasoningEffort,
                currentProjectSession.ServiceTier ?? selectedUiServiceTier);
        }

        if (selectedUiModel != null)
        {
            var selectedUiSettings = currentProjectSession is not null &&
                string.Equals(currentProjectSession.ModelName, selectedUiModel, StringComparison.OrdinalIgnoreCase)
                ? currentProjectSession
                : null;
            return new ModelNameSnapshot(
                selectedUiModel,
                sessionModel?.ModelName,
                selectedUiModel,
                "selected-ui",
                selectedUiReasoningEffort ?? selectedUiSettings?.ReasoningEffort,
                selectedUiSettings?.ServiceTier ?? selectedUiServiceTier);
        }

        if (sessionModel?.ModelName != null)
        {
            return new ModelNameSnapshot(
                selectedUiModel,
                sessionModel.ModelName,
                sessionModel.ModelName,
                "last-session",
                sessionModel.ReasoningEffort,
                sessionModel.ServiceTier ?? selectedUiServiceTier);
        }

        return new ModelNameSnapshot(selectedUiModel, sessionModel?.ModelName, fallback, "fallback");
    }

    private string FallbackModelName()
    {
        return string.IsNullOrWhiteSpace(_presenceOptions.ModelName)
            ? "Codex"
            : _presenceOptions.ModelName;
    }

    private string? DetectFromEnvironment()
    {
        foreach (var variableName in _codexOptions.ModelEnvironmentVariables)
        {
            var value = Environment.GetEnvironmentVariable(variableName);
            if (IsUsableModelName(value))
            {
                return value!.Trim();
            }
        }

        return null;
    }

    private SessionModelDetection? DetectFromRecentSessions(string projectPath)
    {
        var sessionsPath = Path.Combine(_codexHomePath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        var normalizedProjectPath = NormalizePath(projectPath);
        var files = Directory
            .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(Math.Max(1, _codexOptions.RecentSessionFilesToScan));

        var candidates = new List<SessionModelDetection>();

        foreach (var file in files)
        {
            var session = InspectSessionFile(file.FullName, normalizedProjectPath);
            if (!session.HasUsableSettings)
            {
                continue;
            }

            candidates.Add(new SessionModelDetection(
                session.ModelName,
                session.ReasoningEffort,
                session.ServiceTier,
                session.MatchesProject,
                MaxTimestamp(session.LastEventAt, file.LastWriteTimeUtc)));
        }

        return candidates
            .Where(candidate => candidate.IsProjectMatch)
            .OrderByDescending(candidate => candidate.LastActivityAt)
            .FirstOrDefault()
            ?? candidates
                .OrderByDescending(candidate => candidate.LastActivityAt)
                .FirstOrDefault();
    }

    private SessionModelInspection InspectSessionFile(string path, string normalizedProjectPath)
    {
        var matchesProject = false;
        string? modelName = null;
        string? reasoningEffort = null;
        string? serviceTier = null;
        DateTime? lastEventAt = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("\"payload\"", StringComparison.Ordinal) ||
                    (!line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
                     !line.Contains("\"session_meta\"", StringComparison.Ordinal) &&
                     !line.Contains("\"thread_settings_applied\"", StringComparison.Ordinal)))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                var eventTimestamp = TryGetTimestamp(document.RootElement);
                if (eventTimestamp.HasValue &&
                    (!lastEventAt.HasValue || eventTimestamp.Value > lastEventAt.Value))
                {
                    lastEventAt = eventTimestamp;
                }

                if (TryGetString(payload, "cwd", out var cwd) &&
                    NormalizePath(cwd) == normalizedProjectPath)
                {
                    matchesProject = true;
                }

                if (TryGetNestedString(payload, "thread_settings", "cwd", out var threadCwd) &&
                    NormalizePath(threadCwd) == normalizedProjectPath)
                {
                    matchesProject = true;
                }

                if (TryGetString(payload, "model", out var directModel) &&
                    IsUsableModelName(directModel))
                {
                    modelName = directModel;
                }

                if (TryGetNestedString(payload, "thread_settings", "model", out var threadModel) &&
                    IsUsableModelName(threadModel))
                {
                    modelName = threadModel;
                }

                if (payload.TryGetProperty("collaboration_mode", out var collaborationMode) &&
                    collaborationMode.TryGetProperty("settings", out var settings) &&
                    TryGetString(settings, "model", out var collaborationModel) &&
                    IsUsableModelName(collaborationModel))
                {
                    modelName = collaborationModel;
                }

                if (TryGetString(payload, "reasoning_effort", out var directReasoningEffort) &&
                    IsUsableValue(directReasoningEffort))
                {
                    reasoningEffort = directReasoningEffort;
                }

                if (TryGetString(payload, "model_reasoning_effort", out var modelReasoningEffort) &&
                    IsUsableValue(modelReasoningEffort))
                {
                    reasoningEffort = modelReasoningEffort;
                }

                if (TryGetNestedString(payload, "thread_settings", "reasoning_effort", out var threadReasoningEffort) &&
                    IsUsableValue(threadReasoningEffort))
                {
                    reasoningEffort = threadReasoningEffort;
                }

                if (TryGetNestedString(payload, "reasoning", "effort", out var reasoningEffortValue) &&
                    IsUsableValue(reasoningEffortValue))
                {
                    reasoningEffort = reasoningEffortValue;
                }

                if (payload.TryGetProperty("collaboration_mode", out collaborationMode) &&
                    collaborationMode.TryGetProperty("settings", out settings) &&
                    TryGetString(settings, "reasoning_effort", out var collaborationReasoningEffort) &&
                    IsUsableValue(collaborationReasoningEffort))
                {
                    reasoningEffort = collaborationReasoningEffort;
                }

                if (TryGetString(payload, "service_tier", out var directServiceTier) &&
                    IsUsableValue(directServiceTier))
                {
                    serviceTier = directServiceTier;
                }

                if (TryGetNestedString(payload, "thread_settings", "service_tier", out var threadServiceTier) &&
                    IsUsableValue(threadServiceTier))
                {
                    serviceTier = threadServiceTier;
                }
            }
        }
        catch
        {
            return new SessionModelInspection(false, null, null, null, null);
        }

        return new SessionModelInspection(matchesProject, modelName, reasoningEffort, serviceTier, lastEventAt);
    }

    private static bool TryGetNestedString(
        JsonElement element,
        string objectPropertyName,
        string valuePropertyName,
        out string value)
    {
        value = "";
        return element.TryGetProperty(objectPropertyName, out var nested) &&
            TryGetString(nested, valuePropertyName, out value);
    }

    private string? DetectFromConfig()
    {
        var configPath = Path.Combine(_codexHomePath, "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var match = Regex.Match(line, "^\\s*model\\s*=\\s*\"(?<model>[^\"]+)\"\\s*$");
                if (match.Success)
                {
                    var model = match.Groups["model"].Value;
                    return IsUsableModelName(model) ? model.Trim() : null;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private string? DetectReasoningEffortFromConfig()
    {
        var configPath = Path.Combine(_codexHomePath, "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var match = Regex.Match(line, "^\\s*model_reasoning_effort\\s*=\\s*\"(?<effort>[^\"]+)\"\\s*$");
                if (match.Success)
                {
                    var effort = match.Groups["effort"].Value;
                    return IsUsableValue(effort) ? effort.Trim() : null;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private string? DetectServiceTierFromConfig()
    {
        var configPath = Path.Combine(_codexHomePath, "config.toml");
        if (!File.Exists(configPath))
        {
            return null;
        }

        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                var match = Regex.Match(line, "^\\s*service_tier\\s*=\\s*\"(?<tier>[^\"]+)\"\\s*$");
                if (match.Success)
                {
                    var serviceTier = match.Groups["tier"].Value;
                    return IsUsableValue(serviceTier) ? serviceTier.Trim() : null;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private DateTime GetConfigLastWriteTimeUtc()
    {
        var configPath = Path.Combine(_codexHomePath, "config.toml");
        try
        {
            return File.Exists(configPath) ? File.GetLastWriteTimeUtc(configPath) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }

    private static bool IsUsableValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Contains('{', StringComparison.Ordinal) &&
            !value.Contains('}', StringComparison.Ordinal);
    }
    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private static DateTime? TryGetTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return DateTime.TryParse(
            property.GetString(),
            null,
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
    }

    private static DateTime MaxTimestamp(DateTime? first, DateTime second)
    {
        return first.HasValue && first.Value > second ? first.Value : second;
    }

    private static bool IsUsableModelName(string? value)
    {
        return IsUsableValue(value);
    }

    private sealed record SessionModelInspection(
        bool MatchesProject,
        string? ModelName,
        string? ReasoningEffort,
        string? ServiceTier,
        DateTime? LastEventAt)
    {
        public bool HasUsableSettings =>
            IsUsableModelName(ModelName) ||
            IsUsableValue(ReasoningEffort) ||
            IsUsableValue(ServiceTier);
    }

    private sealed record SessionModelDetection(
        string? ModelName,
        string? ReasoningEffort,
        string? ServiceTier,
        bool IsProjectMatch,
        DateTime LastActivityAt);
}

public sealed record ModelNameSnapshot(
    string? SelectedUiModel,
    string? LastUsedSessionModel,
    string FinalDisplayedModel,
    string Source,
    string? ReasoningEffort = null,
    string? ServiceTier = null)
{
    public string DisplayLabel => CodexModelDisplayFormatter.Format(
        FinalDisplayedModel,
        ReasoningEffort,
        ServiceTier);
}

internal static class CodexModelDisplayFormatter
{
    public static string Format(string modelName, string? reasoningEffort, string? serviceTier)
    {
        var parts = new List<string> { FormatModelName(modelName) };

        if (!string.IsNullOrWhiteSpace(reasoningEffort))
        {
            parts.Add(reasoningEffort.Trim().ToLowerInvariant());
        }

        if (IsFastServiceTier(serviceTier) && SupportsOnePointFiveX(modelName))
        {
            parts.Add("1.5x");
        }

        return string.Join(' ', parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private static string FormatModelName(string modelName)
    {
        var trimmed = modelName.Trim();
        return trimmed.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
            ? trimmed.Replace('-', ' ')
            : trimmed;
    }

    private static bool IsFastServiceTier(string? serviceTier)
    {
        return string.Equals(serviceTier?.Trim(), "priority", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(serviceTier?.Trim(), "fast", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsOnePointFiveX(string modelName)
    {
        var normalized = modelName.Trim().ToLowerInvariant();
        return normalized is "gpt-5.6" or "gpt-5.5" or "gpt-5.4" ||
            normalized.StartsWith("gpt-5.6-", StringComparison.Ordinal) ||
            normalized.StartsWith("gpt-5.5-", StringComparison.Ordinal) ||
            normalized.StartsWith("gpt-5.4-", StringComparison.Ordinal);
    }
}
