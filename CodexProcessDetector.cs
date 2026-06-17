using System.Diagnostics;
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

    public CodexProcessSnapshot GetSnapshot(
        string? projectPath = null,
        ProjectSnapshot? projectSnapshot = null,
        GitSnapshot? gitSnapshot = null)
    {
        var sessionInspection = InspectRecentSessions(projectPath);
        var matchedProcessName = FindMatchingProcessName();
        var isRunning = matchedProcessName is not null ||
            (sessionInspection is not null &&
             sessionInspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes));

        if (!isRunning)
        {
            return new CodexProcessSnapshot(false, null, false)
            {
                DetectedActivityKind = CodexActivityKind.Offline,
                ActivityProvenance = ActivityProvenance.Observed,
                ActivityReason = "Codex process, window, and recent session activity were not detected."
            };
        }

        var recentEditedFiles = GetRecentEditedFiles(projectSnapshot);
        var changedFileCount = gitSnapshot?.ChangedFileCount ?? 0;
        var activity = DetermineActivity(
            recentEditedFiles,
            changedFileCount,
            sessionInspection,
            out var provenance,
            out var reason,
            out var lastObservedAt);

        return new CodexProcessSnapshot(true, matchedProcessName, activity.IsActive())
        {
            DetectedActivityKind = activity,
            ActivityProvenance = provenance,
            ActivityReason = reason,
            LastObservedAt = lastObservedAt
        };
    }

    public bool DetermineIfThinking(string? projectPath = null)
    {
        return GetSnapshot(projectPath).IsThinking;
    }

    private IReadOnlyList<RecentProjectFileSnapshot> GetRecentEditedFiles(ProjectSnapshot? projectSnapshot)
    {
        if (projectSnapshot is null)
        {
            return Array.Empty<RecentProjectFileSnapshot>();
        }

        var freshnessSeconds = Math.Max(5, _presenceOptions.EditingFreshnessSeconds);
        var cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(freshnessSeconds);
        return projectSnapshot.RecentFiles
            .Where(file => file.LastWriteTimeUtc >= cutoff)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ToArray();
    }

    private CodexActivityKind DetermineActivity(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        int changedFileCount,
        SessionInspection? sessionInspection,
        out ActivityProvenance provenance,
        out string reason,
        out DateTime? lastObservedAt)
    {
        lastObservedAt = sessionInspection?.LastObservedAt ?? recentEditedFiles.FirstOrDefault()?.LastWriteTimeUtc;
        var hasFreshSession = sessionInspection is not null &&
            sessionInspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes);

        if (sessionInspection?.CollaborationMode is "plan" && hasFreshSession)
        {
            provenance = ActivityProvenance.Observed;
            reason = "turn_context collaboration_mode=plan";
            return CodexActivityKind.Planning;
        }

        if (recentEditedFiles.Count >= 4 ||
            changedFileCount >= 4 ||
            (recentEditedFiles.Count >= 2 && changedFileCount >= 2))
        {
            provenance = ActivityProvenance.Observed;
            reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
            return CodexActivityKind.Refactoring;
        }

        if (recentEditedFiles.Count > 0 || changedFileCount > 0)
        {
            provenance = ActivityProvenance.Observed;
            reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
            return CodexActivityKind.ApplyingEdits;
        }

        if (hasFreshSession && sessionInspection?.HasTaskCompleted == true && !sessionInspection.HasTaskStarted)
        {
            provenance = ActivityProvenance.Observed;
            reason = "task_complete without task_started";
            return CodexActivityKind.Ready;
        }

        if (hasFreshSession && sessionInspection?.HasTaskCompletedSinceStart == true)
        {
            provenance = ActivityProvenance.Observed;
            reason = "task_complete without file writes";
            return CodexActivityKind.Ready;
        }

        if (hasFreshSession && sessionInspection?.HasTaskStarted == true && !sessionInspection.HasTaskCompletedSinceStart)
        {
            provenance = ActivityProvenance.Inferred;
            reason = "task_started without file writes";
            return CodexActivityKind.Analyzing;
        }

        if (hasFreshSession)
        {
            provenance = ActivityProvenance.Inferred;
            reason = "recent Codex activity without file writes";
            return CodexActivityKind.Analyzing;
        }

        provenance = ActivityProvenance.Inferred;
        reason = "Codex running but idle";
        return CodexActivityKind.Ready;
    }

    private SessionInspection? InspectRecentSessions(string? projectPath)
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

    private SessionInspection AnalyzeSessionFile(string path, string? normalizedProjectPath)
    {
        var hasProjectPath = false;
        var matchesProject = false;
        var hasTaskStarted = false;
        var hasTaskCompleted = false;
        DateTime? lastTaskStartedAt = null;
        DateTime? lastTaskCompletedAt = null;
        DateTime? lastObservedAt = null;
        string? collaborationMode = null;

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
            }
        }
        catch
        {
            // Fall through with what we were able to infer.
        }

        return new SessionInspection(
            hasProjectPath,
            matchesProject,
            hasTaskStarted,
            hasTaskCompleted,
            lastTaskStartedAt,
            lastTaskCompletedAt,
            lastObservedAt,
            collaborationMode);
    }

    private static string? TryGetCollaborationMode(JsonElement payload)
    {
        if (payload.TryGetProperty("collaboration_mode", out var collaborationMode))
        {
            if (TryGetString(collaborationMode, "mode", out var mode))
            {
                return mode;
            }
        }

        if (TryGetString(payload, "collaboration_mode_kind", out var collaborationModeKind))
        {
            return collaborationModeKind;
        }

        return null;
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

    private string? FindMatchingProcessName()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Matches(process.ProcessName, _options.ProcessNameContains) ||
                    Matches(process.MainWindowTitle, _options.WindowTitleContains))
                {
                    return process.ProcessName;
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

    private sealed record SessionInspection(
        bool HasProjectPath,
        bool MatchesProject,
        bool HasTaskStarted,
        bool HasTaskCompleted,
        DateTime? LastTaskStartedAt,
        DateTime? LastTaskCompletedAt,
        DateTime? LastObservedAt,
        string? CollaborationMode)
    {
        public bool HasRecentActivity(int staleTimeoutMinutes)
        {
            var freshest = LastObservedAt ?? LastTaskStartedAt ?? LastTaskCompletedAt;
            if (!freshest.HasValue)
            {
                return false;
            }

            return DateTime.UtcNow - freshest.Value <= TimeSpan.FromMinutes(staleTimeoutMinutes);
        }

        public bool HasTaskCompletedSinceStart =>
            HasTaskStarted &&
            HasTaskCompleted &&
            LastTaskStartedAt.HasValue &&
            LastTaskCompletedAt.HasValue &&
            LastTaskCompletedAt >= LastTaskStartedAt;
    }
}
