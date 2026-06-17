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
                Confidence = ActivityConfidence.High,
                ActivityReason = "Codex process, window, and recent session activity were not detected."
            };
        }

        var recentEditedFiles = GetRecentEditedFiles(projectSnapshot);
        var changedFileCount = gitSnapshot?.ChangedFileCount ?? 0;
        var activity = DetermineActivity(
            recentEditedFiles,
            changedFileCount,
            sessionInspection,
            gitSnapshot,
            out var provenance,
            out var confidence,
            out var reason,
            out var lastObservedAt);

        return new CodexProcessSnapshot(true, matchedProcessName, activity.IsActive())
        {
            DetectedActivityKind = activity,
            ActivityProvenance = provenance,
            Confidence = confidence,
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
        GitSnapshot? gitSnapshot,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason,
        out DateTime? lastObservedAt)
    {
        lastObservedAt = MaxTimestamp(
            sessionInspection?.LastObservedAt,
            recentEditedFiles.FirstOrDefault()?.LastWriteTimeUtc);

        var hasFreshSession = sessionInspection is not null &&
            sessionInspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes);
        var hasRecentEdits = recentEditedFiles.Count > 0;
        var hasMultipleRecentEdits = recentEditedFiles.Count >= 2 || changedFileCount >= 2;
        var hasRefactorEvidence = HasRefactorEvidence(recentEditedFiles, sessionInspection, gitSnapshot);

        if (sessionInspection?.HasRunningCommand == true && hasFreshSession)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = sessionInspection.RunningCommandReason ?? "pending shell_command function call in session log";
            return CodexActivityKind.RunningCommand;
        }

        if (hasRefactorEvidence)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.Low;
            reason = BuildRefactorReason(recentEditedFiles, sessionInspection, gitSnapshot);
            return CodexActivityKind.Refactoring;
        }

        if (hasRecentEdits)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            if (hasMultipleRecentEdits)
            {
                reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
                return CodexActivityKind.UpdatingFiles;
            }

            reason = $"recent edit={recentEditedFiles[0].Name}, git changed files={changedFileCount}";
            return CodexActivityKind.ApplyingEdits;
        }

        if (sessionInspection?.CollaborationMode is "plan" && hasFreshSession)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.Low;
            reason = "turn_context collaboration_mode=plan";
            return CodexActivityKind.Planning;
        }

        if (sessionInspection?.HasTaskCompleted == true && !sessionInspection.HasTaskStarted)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = "task_complete without task_started";
            return CodexActivityKind.Ready;
        }

        if (sessionInspection?.HasTaskCompletedSinceStart == true)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = "task_complete without file writes";
            return CodexActivityKind.Ready;
        }

        if (hasFreshSession && sessionInspection?.HasTaskStarted == true)
        {
            provenance = ActivityProvenance.Inferred;
            confidence = ActivityConfidence.High;
            reason = "task_started without recent file writes";
            return CodexActivityKind.AnalyzingProject;
        }

        if (hasFreshSession)
        {
            provenance = ActivityProvenance.Inferred;
            confidence = ActivityConfidence.High;
            reason = "recent Codex activity without file writes";
            return CodexActivityKind.AnalyzingProject;
        }

        provenance = ActivityProvenance.Inferred;
        confidence = ActivityConfidence.High;
        reason = "Codex running but idle";
        return CodexActivityKind.Ready;
    }

    private static DateTime? MaxTimestamp(params DateTime?[] timestamps)
    {
        DateTime? max = null;
        foreach (var timestamp in timestamps)
        {
            if (!timestamp.HasValue)
            {
                continue;
            }

            if (!max.HasValue || timestamp.Value > max.Value)
            {
                max = timestamp;
            }
        }

        return max;
    }

    private static string BuildRefactorReason(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        SessionInspection? sessionInspection,
        GitSnapshot? gitSnapshot)
    {
        if (gitSnapshot?.LatestCommitMessage is { Length: > 0 } commitMessage && ContainsAny(commitMessage, RefactorKeywords))
        {
            return $"git commit message suggests refactor: {commitMessage}";
        }

        var recentFile = recentEditedFiles.FirstOrDefault(file =>
            ContainsAny(file.Name, RefactorKeywords) || ContainsAny(file.Path, RefactorKeywords));
        if (recentFile is not null)
        {
            return $"recent file name suggests refactor: {recentFile.Name}";
        }

        if (!string.IsNullOrWhiteSpace(sessionInspection?.RefactorEvidenceReason))
        {
            return sessionInspection.RefactorEvidenceReason!;
        }

        return "refactor hint detected in session or git metadata";
    }

    private static bool HasRefactorEvidence(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        SessionInspection? sessionInspection,
        GitSnapshot? gitSnapshot)
    {
        if (gitSnapshot?.LatestCommitMessage is { Length: > 0 } commitMessage &&
            ContainsAny(commitMessage, RefactorKeywords))
        {
            return true;
        }

        if (recentEditedFiles.Any(file =>
                ContainsAny(file.Name, RefactorKeywords) ||
                ContainsAny(file.Path, RefactorKeywords)))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(sessionInspection?.RefactorEvidenceReason))
        {
            return true;
        }

        return false;
    }

    private static readonly string[] RefactorKeywords =
    [
        "refactor",
        "refactoring",
        "restructure",
        "reorganize",
        "cleanup",
        "clean up",
        "rename",
        "extract",
        "split",
        "migrate"
    ];

    private static bool ContainsAny(string value, IEnumerable<string> needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
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
        var pendingShellCommands = new HashSet<string>(StringComparer.Ordinal);
        var completedShellCommands = new HashSet<string>(StringComparer.Ordinal);
        string? runningCommandReason = null;
        string? refactorEvidenceReason = null;

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

                if (refactorEvidenceReason is null && ContainsAny(line, RefactorKeywords))
                {
                    refactorEvidenceReason = $"session log hint: {CompactLine(line)}";
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
            refactorEvidenceReason);
    }

    private static string CompactLine(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length <= 120 ? trimmed : trimmed[..117] + "...";
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
        string? CollaborationMode,
        bool HasRunningCommand,
        string? RunningCommandReason,
        string? RefactorEvidenceReason)
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
