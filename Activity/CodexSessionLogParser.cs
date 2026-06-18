using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class CodexSessionLogParser
{
    private readonly CodexDetectionOptions _options;

    public CodexSessionLogParser(CodexDetectionOptions options)
    {
        _options = options;
    }

    public SessionInspection? InspectRecentSessions(string? projectPath)
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        try
        {
            var normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : NormalizePath(projectPath);

            var files = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RecentSessionFilesToScan))
                .ToArray();

            SessionInspection? latestAny = null;
            SessionInspection? latestProjectMatch = null;

            foreach (var file in files)
            {
                var inspection = AnalyzeSessionFile(file.FullName, normalizedProjectPath);
                latestAny ??= inspection;

                if (inspection.MatchesProject)
                {
                    latestProjectMatch = inspection;
                    break;
                }
            }

            return latestProjectMatch ?? latestAny;
        }
        catch
        {
            return null;
        }
    }

    public string? GetLatestObservedProjectPath()
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        try
        {
            var files = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RecentSessionFilesToScan));

            foreach (var file in files)
            {
                var inspection = AnalyzeSessionFile(file.FullName, normalizedProjectPath: null);
                if (!string.IsNullOrWhiteSpace(inspection.ProjectPath))
                {
                    return inspection.ProjectPath;
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private SessionInspection AnalyzeSessionFile(string path, string? normalizedProjectPath)
    {
        var hasProjectPath = false;
        var matchesProject = false;
        string? latestProjectPath = null;
        var hasTaskStarted = false;
        var hasTaskCompleted = false;
        DateTime? lastTaskStartedAt = null;
        DateTime? lastTaskCompletedAt = null;
        DateTime? lastObservedAt = null;
        string? collaborationMode = null;
        var pendingShellCommands = new HashSet<string>(StringComparer.Ordinal);
        var completedShellCommands = new HashSet<string>(StringComparer.Ordinal);
        string? runningCommandReason = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains("\"payload\"", StringComparison.Ordinal))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                var timestamp = TryGetTimestamp(document.RootElement);
                if (timestamp.HasValue)
                {
                    lastObservedAt = timestamp;
                }

                if (TryGetString(payload, "cwd", out var cwd))
                {
                    hasProjectPath = true;
                    latestProjectPath = cwd;
                    if (normalizedProjectPath != null && NormalizePath(cwd) == normalizedProjectPath)
                    {
                        matchesProject = true;
                    }
                }

                var payloadType = TryGetString(payload, "type", out var type) ? type : null;

                if (payloadType is "task_started" || line.Contains("\"task_started\"", StringComparison.Ordinal))
                {
                    hasTaskStarted = true;
                    if (timestamp.HasValue)
                    {
                        lastTaskStartedAt = timestamp;
                    }

                    var mode = TryGetCollaborationMode(payload);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        collaborationMode = mode;
                    }
                }

                if (payloadType is "task_complete" || line.Contains("\"task_complete\"", StringComparison.Ordinal))
                {
                    hasTaskCompleted = true;
                    if (timestamp.HasValue)
                    {
                        lastTaskCompletedAt = timestamp;
                    }
                }

                if (payloadType is "turn_context")
                {
                    var mode = TryGetCollaborationMode(payload);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        collaborationMode = mode;
                    }
                }

                if (payloadType is "function_call" &&
                    TryGetString(payload, "name", out var functionName) &&
                    string.Equals(functionName, "shell_command", StringComparison.OrdinalIgnoreCase) &&
                    TryGetString(payload, "call_id", out var callId))
                {
                    pendingShellCommands.Add(callId);
                }

                if (payloadType is "function_call_output" &&
                    TryGetString(payload, "call_id", out var outputCallId))
                {
                    completedShellCommands.Add(outputCallId);
                }

                if (runningCommandReason is null &&
                    payloadType is "function_call" &&
                    TryGetString(payload, "name", out var callName) &&
                    string.Equals(callName, "shell_command", StringComparison.OrdinalIgnoreCase))
                {
                    runningCommandReason = "pending shell_command function call in session log";
                }
            }
        }
        catch
        {
            // Fall through with what we were able to infer.
        }

        var hasRunningCommand = pendingShellCommands.Except(completedShellCommands).Any();
        if (hasRunningCommand && runningCommandReason is null)
        {
            runningCommandReason = "pending shell_command function call in session log";
        }

        return new SessionInspection(
            hasProjectPath,
            matchesProject,
            hasTaskStarted,
            hasTaskCompleted,
            lastTaskStartedAt,
            lastTaskCompletedAt,
            lastObservedAt,
            collaborationMode,
            hasRunningCommand,
            runningCommandReason,
            null)
        {
            ProjectPath = latestProjectPath
        };
    }

    private static string? TryGetCollaborationMode(JsonElement payload)
    {
        string? mode = null;

        if (payload.TryGetProperty("collaboration_mode", out var collaborationMode))
        {
            mode = TryGetNormalizedCollaborationMode(collaborationMode, "mode");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            mode = TryGetNormalizedCollaborationMode(collaborationMode, "kind");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            mode = TryGetNormalizedCollaborationMode(collaborationMode, "value");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            if (collaborationMode.TryGetProperty("settings", out var settings))
            {
                mode = TryGetNormalizedCollaborationMode(settings, "mode");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }

                mode = TryGetNormalizedCollaborationMode(settings, "kind");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }

                mode = TryGetNormalizedCollaborationMode(settings, "value");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }
            }
        }

        mode = TryGetNormalizedCollaborationMode(payload, "collaboration_mode_kind");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        mode = TryGetNormalizedCollaborationMode(payload, "collaboration_mode_mode");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        return null;
    }

    private static string? TryGetNormalizedCollaborationMode(JsonElement element, string propertyName)
    {
        if (!TryGetString(element, propertyName, out var value))
        {
            return null;
        }

        return NormalizeCollaborationMode(value);
    }

    private static string? NormalizeCollaborationMode(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return compact switch
        {
            "plan" or "planmode" => "plan",
            "goal" or "goalmode" => "plan",
            _ => trimmed.ToLowerInvariant()
        };
    }

    private static DateTime? TryGetTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var timeStr = prop.GetString();
        if (DateTime.TryParse(timeStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return dt;
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
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
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }
}
