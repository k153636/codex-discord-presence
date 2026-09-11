using System.IO;
using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

public sealed class CodexModelNameProvider
{
    private readonly CodexDetectionOptions _codexOptions;
    private readonly PresenceTemplateOptions _presenceOptions;
    private readonly CodexSessionLogParser _sessionLogParser;
    private readonly string _codexHomePath;

    public CodexModelNameProvider(CodexDetectionOptions codexOptions, PresenceTemplateOptions presenceOptions)
        : this(codexOptions, presenceOptions, new CodexSessionLogParser(codexOptions, presenceOptions))
    {
    }

    internal CodexModelNameProvider(
        CodexDetectionOptions codexOptions,
        PresenceTemplateOptions presenceOptions,
        CodexSessionLogParser sessionLogParser)
    {
        _codexOptions = codexOptions;
        _presenceOptions = presenceOptions;
        _sessionLogParser = sessionLogParser;
        _codexHomePath = codexOptions.GetResolvedHomePath();
    }

    public string GetModelName(string projectPath)
    {
        return GetSnapshot(projectPath).DisplayLabel;
    }

    public ModelNameSnapshot GetSnapshot(string projectPath)
    {
        return GetSnapshot(projectPath, includeSessionScan: true);
    }

    public ModelNameSnapshot GetSnapshot(
        string projectPath,
        bool includeSessionScan,
        CancellationToken cancellationToken = default)
    {
        var sessionInspection = includeSessionScan
            ? DetectFromRecentSessions(projectPath, cancellationToken)
            : null;
        return GetSnapshotCore(sessionInspection);
    }

    internal ModelNameSnapshot GetSnapshotForSession(SessionInspection? sessionInspection)
    {
        var primarySessionInspection = sessionInspection?.IsPrimaryThread == true
            ? sessionInspection
            : null;
        return GetSnapshotCore(primarySessionInspection?.HasUsableModelSettings == true
            ? primarySessionInspection
            : null);
    }

    private ModelNameSnapshot GetSnapshotCore(SessionInspection? sessionModel)
    {
        var fallback = FallbackModelName();
        var environmentModel = DetectFromEnvironment();
        var configSettings = ReadConfigSettings();
        var selectedUiModel = configSettings.ModelName;
        var selectedUiReasoningEffort = configSettings.ReasoningEffort;
        var selectedUiServiceTier = configSettings.ServiceTier;
        var selectedUiModelTime = GetConfigLastWriteTimeUtc();
        var currentProjectSession = sessionModel is { MatchesProject: true } &&
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

    private SessionInspection? DetectFromRecentSessions(
        string projectPath,
        CancellationToken cancellationToken)
    {
        var sessionInspection = _sessionLogParser.InspectRecentSessions(projectPath, cancellationToken);
        return sessionInspection?.HasUsableModelSettings == true
            ? sessionInspection
            : null;
    }

    private ConfigModelSettings ReadConfigSettings()
    {
        var configPath = Path.Combine(_codexHomePath, "config.toml");
        if (!File.Exists(configPath))
        {
            return new ConfigModelSettings(null, null, null);
        }

        string? modelName = null;
        string? reasoningEffort = null;
        string? serviceTier = null;
        try
        {
            foreach (var line in File.ReadLines(configPath))
            {
                if (modelName is null)
                {
                    modelName = ReadConfigValue(line, "^\\s*model\\s*=\\s*\"(?<value>[^\"]+)\"\\s*$");
                }

                if (reasoningEffort is null)
                {
                    reasoningEffort = ReadConfigValue(line, "^\\s*model_reasoning_effort\\s*=\\s*\"(?<value>[^\"]+)\"\\s*$");
                }

                if (serviceTier is null)
                {
                    serviceTier = ReadConfigValue(line, "^\\s*service_tier\\s*=\\s*\"(?<value>[^\"]+)\"\\s*$");
                }

                if (modelName is not null && reasoningEffort is not null && serviceTier is not null)
                {
                    break;
                }
            }
        }
        catch
        {
            return new ConfigModelSettings(null, null, null);
        }

        return new ConfigModelSettings(modelName, reasoningEffort, serviceTier);
    }

    private static string? ReadConfigValue(string line, string pattern)
    {
        var match = Regex.Match(line, pattern);
        if (!match.Success)
        {
            return null;
        }

        var value = match.Groups["value"].Value;
        return IsUsableValue(value) ? value.Trim() : null;
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

    private static bool IsUsableValue(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Contains('{', StringComparison.Ordinal) &&
            !value.Contains('}', StringComparison.Ordinal);
    }

    private static bool IsUsableModelName(string? value)
    {
        return IsUsableValue(value);
    }

    private readonly record struct ConfigModelSettings(
        string? ModelName,
        string? ReasoningEffort,
        string? ServiceTier);
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
