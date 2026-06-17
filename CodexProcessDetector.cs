using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class CodexProcessDetector
{
    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;

    public CodexProcessDetector(CodexDetectionOptions options, PresenceTemplateOptions presenceOptions)
    {
        _options = options;
        _presenceOptions = presenceOptions;
    }

    public CodexProcessSnapshot GetSnapshot(string? projectPath = null)
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Matches(process.ProcessName, _options.ProcessNameContains) ||
                    Matches(process.MainWindowTitle, _options.WindowTitleContains))
                {
                    var isThinking = DetermineIfThinking(projectPath);
                    return new CodexProcessSnapshot(true, process.ProcessName, isThinking);
                }
            }
            catch
            {
                // Some system processes deny metadata access. They are irrelevant for Codex detection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return new CodexProcessSnapshot(false, null, false);
    }

    internal bool DetermineIfThinking(string? projectPath = null)
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return false;
        }

        try
        {
            var files = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RecentSessionFilesToScan))
                .ToArray();

            if (files.Length == 0)
            {
                return false;
            }

            var normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : NormalizePath(projectPath);
            SessionThinkingInspection? latestAnySession = null;
            var sawProjectAwareSession = false;

            foreach (var file in files)
            {
                var inspection = AnalyzeSessionFileForThinking(file.FullName, normalizedProjectPath);
                latestAnySession ??= inspection;

                if (inspection.HasProjectPath)
                {
                    sawProjectAwareSession = true;
                }

                if (normalizedProjectPath != null && inspection.MatchesProject)
                {
                    return inspection.IsThinking;
                }
            }

            if (normalizedProjectPath != null && sawProjectAwareSession)
            {
                return false;
            }

            return latestAnySession?.IsThinking ?? false;
        }
        catch
        {
            return false;
        }
    }

    private SessionThinkingInspection AnalyzeSessionFileForThinking(string path, string? normalizedProjectPath)
    {
        bool isThinking = false;
        bool hasProjectPath = false;
        bool matchesProject = false;
        DateTime? lastTaskStartedTime = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var payloadType = TryReadPayloadTypeAndCwd(line, out var cwd);
                if (!string.IsNullOrWhiteSpace(cwd))
                {
                    hasProjectPath = true;
                    if (normalizedProjectPath != null && NormalizePath(cwd) == normalizedProjectPath)
                    {
                        matchesProject = true;
                    }
                }

                if (string.Equals(payloadType, "task_started", StringComparison.Ordinal) ||
                    line.Contains("\"task_started\"", StringComparison.Ordinal))
                {
                    isThinking = true;
                    lastTaskStartedTime = ExtractTimestamp(line);
                }
                else if (string.Equals(payloadType, "task_complete", StringComparison.Ordinal) ||
                         line.Contains("\"task_complete\"", StringComparison.Ordinal))
                {
                    isThinking = false;
                }
            }
        }
        catch
        {
            // Return current state found up to exception, or false
        }

        if (isThinking && lastTaskStartedTime.HasValue)
        {
            var elapsed = DateTime.UtcNow - lastTaskStartedTime.Value;
            if (elapsed.TotalMinutes >= _presenceOptions.ThinkingStaleTimeoutMinutes)
            {
                isThinking = false;
            }
        }

        return new SessionThinkingInspection(isThinking, hasProjectPath, matchesProject);
    }

    private static string? TryReadPayloadTypeAndCwd(string line, out string? cwd)
    {
        cwd = null;
        if (!line.Contains("\"payload\"", StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(line);
            if (!doc.RootElement.TryGetProperty("payload", out var payload))
            {
                return null;
            }

            if (payload.TryGetProperty("cwd", out var cwdProperty) &&
                cwdProperty.ValueKind == JsonValueKind.String)
            {
                cwd = cwdProperty.GetString();
            }

            if (payload.TryGetProperty("type", out var typeProperty) &&
                typeProperty.ValueKind == JsonValueKind.String)
            {
                return typeProperty.GetString();
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static DateTime? ExtractTimestamp(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("timestamp", out var prop))
            {
                var timeStr = prop.GetString();
                if (DateTime.TryParse(timeStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    return dt;
                }
            }
        }
        catch
        {
            // Ignore parse errors
        }
        return null;
    }

    private static bool Matches(string? value, IEnumerable<string> needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return needles.Any(needle =>
            !string.IsNullOrWhiteSpace(needle) &&
            value.Contains(needle, StringComparison.OrdinalIgnoreCase));
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

    private sealed record SessionThinkingInspection(bool IsThinking, bool HasProjectPath, bool MatchesProject);
}
