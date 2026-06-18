using System.Diagnostics;
namespace CodexDiscordPresence;

public sealed class CodexProcessDetector
{
    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;
    private readonly CodexSessionLogParser _sessionLogParser;
    private readonly Dictionary<string, DateTime> _lastObservedEditedFiles = new(StringComparer.OrdinalIgnoreCase);

    public CodexProcessDetector(CodexDetectionOptions options, PresenceTemplateOptions presenceOptions)
    {
        _options = options;
        _presenceOptions = presenceOptions;
        _sessionLogParser = new CodexSessionLogParser(options);
    }

    public CodexProcessSnapshot GetSnapshot(
        string? projectPath = null,
        ProjectSnapshot? projectSnapshot = null,
        GitSnapshot? gitSnapshot = null,
        CodexActivityKind? previousActivityKind = null)
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
            previousActivityKind,
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
            CollaborationMode = sessionInspection?.CollaborationMode,
            LastObservedAt = lastObservedAt,
            RecentEditedFiles = recentEditedFiles
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

        var now = DateTime.UtcNow;
        var startupWindow = TimeSpan.FromSeconds(5);
        var changedFiles = new List<RecentProjectFileSnapshot>();
        foreach (var file in projectSnapshot.RecentFiles.OrderByDescending(file => file.LastWriteTimeUtc))
        {
            var normalizedPath = NormalizePath(file.Path);
            var isVeryRecent = now - file.LastWriteTimeUtc <= startupWindow;
            if (!_lastObservedEditedFiles.TryGetValue(normalizedPath, out var lastSeen))
            {
                if (isVeryRecent)
                {
                    changedFiles.Add(file);
                }
            }
            else if (file.LastWriteTimeUtc > lastSeen)
            {
                changedFiles.Add(file);
            }

            _lastObservedEditedFiles[normalizedPath] = file.LastWriteTimeUtc;
        }

        return changedFiles;
    }

    private CodexActivityKind DetermineActivity(
        IReadOnlyList<RecentProjectFileSnapshot> recentEditedFiles,
        int changedFileCount,
        SessionInspection? sessionInspection,
        GitSnapshot? gitSnapshot,
        CodexActivityKind? previousActivityKind,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason,
        out DateTime? lastObservedAt)
    {
        lastObservedAt = MaxTimestamp(
            sessionInspection?.LastObservedAt,
            recentEditedFiles.FirstOrDefault()?.LastWriteTimeUtc);

        var createdFileCount = gitSnapshot?.CreatedFileCount ?? 0;
        var deletedFileCount = gitSnapshot?.DeletedFileCount ?? 0;
        var hasFreshSession = sessionInspection is not null &&
            sessionInspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes);
        var hasFreshRecentEdits = CodexActivityEvidence.HasFreshRecentEdits(recentEditedFiles, _presenceOptions.EditingFreshnessSeconds);
        var hasBurstRecentEdits = CodexActivityEvidence.HasBurstRecentEdits(recentEditedFiles, changedFileCount);
        var hasRefactorEvidence = CodexActivityEvidence.HasRefactorEvidence(gitSnapshot);
        var hasCreatingEvidence = createdFileCount > 0 &&
            deletedFileCount == 0 &&
            changedFileCount == createdFileCount;
        var hasDeletingEvidence = deletedFileCount > 0 &&
            createdFileCount == 0 &&
            changedFileCount == deletedFileCount;

        if (sessionInspection?.HasRunningCommand == true && hasFreshSession)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = sessionInspection.RunningCommandReason ?? "pending shell_command function call in session log";
            return CodexActivityKind.RunningCommand;
        }

        if (hasFreshRecentEdits)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            if (hasBurstRecentEdits)
            {
                reason = $"recent edits burst={recentEditedFiles.Count}, git changed files={changedFileCount}";
                return CodexActivityKind.UpdatingFiles;
            }

            if (hasBurstRecentEdits || recentEditedFiles.Count > 1)
            {
                reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
                return CodexActivityKind.UpdatingFiles;
            }

            reason = $"recent edit={recentEditedFiles[0].Name}, git changed files={changedFileCount}";
            return CodexActivityKind.ApplyingEdits;
        }

        if (hasCreatingEvidence)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = $"created files={createdFileCount}, git changed files={changedFileCount}";
            return CodexActivityKind.CreatingFiles;
        }

        if (hasDeletingEvidence)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = $"deleted files={deletedFileCount}, git changed files={changedFileCount}";
            return CodexActivityKind.DeletingFiles;
        }

        if (sessionInspection?.CollaborationMode is "plan" && hasFreshSession)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.Low;
            reason = "turn_context collaboration_mode=plan";
            return CodexActivityKind.Planning;
        }

        if (hasRefactorEvidence)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.Low;
            reason = CodexActivityEvidence.BuildRefactorReason(sessionInspection, gitSnapshot);
            return CodexActivityKind.Refactoring;
        }

        if (previousActivityKind == CodexActivityKind.AnalyzingProject &&
            hasFreshSession &&
            sessionInspection?.HasTaskStarted == true &&
            changedFileCount > 0 &&
            recentEditedFiles.Count == 0)
        {
            provenance = ActivityProvenance.Mixed;
            confidence = ActivityConfidence.High;
            reason = $"task_started with git changed files={changedFileCount}";
            return changedFileCount > 1
                ? CodexActivityKind.UpdatingFiles
                : CodexActivityKind.ApplyingEdits;
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

    private SessionInspection? InspectRecentSessions(string? projectPath)
    {
        return _sessionLogParser.InspectRecentSessions(projectPath);
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

}
