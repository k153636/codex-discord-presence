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
        return GetSnapshot(projectPath).FinalDisplayedModel;
    }

    public ModelNameSnapshot GetSnapshot(string projectPath)
    {
        var fallback = FallbackModelName();
        var environmentModel = DetectFromEnvironment();
        var selectedUiModel = DetectFromConfig();
        var selectedUiModelTime = GetConfigLastWriteTimeUtc();
        var sessionModel = DetectFromRecentSessions(projectPath);

        if (!_presenceOptions.AutoDetectModelName)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel?.ModelName, fallback, "fallback");
        }

        if (environmentModel != null)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel?.ModelName, environmentModel, "environment");
        }

        if (sessionModel is { IsProjectMatch: true } &&
            sessionModel.LastWriteTimeUtc >= selectedUiModelTime &&
            sessionModel.ModelName != null)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel.ModelName, sessionModel.ModelName, "project-session");
        }

        if (selectedUiModel != null)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel?.ModelName, selectedUiModel, "selected-ui");
        }

        if (sessionModel?.ModelName != null)
        {
            return new ModelNameSnapshot(selectedUiModel, sessionModel.ModelName, sessionModel.ModelName, "last-session");
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

        SessionModelDetection? newestAnyProjectModel = null;

        foreach (var file in files)
        {
            var session = InspectSessionFile(file.FullName, normalizedProjectPath);
            if (session.MatchesProject && IsUsableModelName(session.ModelName))
            {
                return new SessionModelDetection(session.ModelName!, true, file.LastWriteTimeUtc);
            }

            if (newestAnyProjectModel is null && IsUsableModelName(session.ModelName))
            {
                newestAnyProjectModel = new SessionModelDetection(session.ModelName!, false, file.LastWriteTimeUtc);
            }
        }

        return newestAnyProjectModel;
    }

    private SessionModelInspection InspectSessionFile(string path, string normalizedProjectPath)
    {
        var matchesProject = false;
        string? modelName = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("\"payload\"", StringComparison.Ordinal) ||
                    (!line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
                     !line.Contains("\"session_meta\"", StringComparison.Ordinal)))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                if (TryGetString(payload, "cwd", out var cwd) &&
                    NormalizePath(cwd) == normalizedProjectPath)
                {
                    matchesProject = true;
                }

                if (TryGetString(payload, "model", out var directModel) &&
                    IsUsableModelName(directModel))
                {
                    modelName = directModel;
                }

                if (payload.TryGetProperty("collaboration_mode", out var collaborationMode) &&
                    collaborationMode.TryGetProperty("settings", out var settings) &&
                    TryGetString(settings, "model", out var collaborationModel) &&
                    IsUsableModelName(collaborationModel))
                {
                    modelName = collaborationModel;
                }
            }
        }
        catch
        {
            return new SessionModelInspection(false, null);
        }

        return new SessionModelInspection(matchesProject, modelName);
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

    private static bool IsUsableModelName(string? value)
    {
        return !string.IsNullOrWhiteSpace(value) &&
            !value.Contains('{', StringComparison.Ordinal) &&
            !value.Contains('}', StringComparison.Ordinal);
    }

    private sealed record SessionModelInspection(bool MatchesProject, string? ModelName);
    private sealed record SessionModelDetection(string ModelName, bool IsProjectMatch, DateTime LastWriteTimeUtc);
}

public sealed record ModelNameSnapshot(
    string? SelectedUiModel,
    string? LastUsedSessionModel,
    string FinalDisplayedModel,
    string Source);
