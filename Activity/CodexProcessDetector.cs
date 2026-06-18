namespace CodexDiscordPresence;

public sealed class CodexProcessDetector
{
    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;
    private readonly CodexProcessNameMatcher _processNameMatcher;
    private readonly CodexActivityResolver _activityResolver;
    private readonly CodexSessionLogParser _sessionLogParser;
    private readonly RecentEditedFileTracker _recentEditedFileTracker = new();

    public CodexProcessDetector(CodexDetectionOptions options, PresenceTemplateOptions presenceOptions)
    {
        _options = options;
        _presenceOptions = presenceOptions;
        _processNameMatcher = new CodexProcessNameMatcher(options);
        _activityResolver = new CodexActivityResolver();
        _sessionLogParser = new CodexSessionLogParser(options, presenceOptions);
    }

    public CodexProcessSnapshot GetSnapshot(
        string? projectPath = null,
        ProjectSnapshot? projectSnapshot = null,
        GitSnapshot? gitSnapshot = null,
        CodexActivityKind? previousActivityKind = null)
    {
        var sessionInspection = InspectRecentSessions(projectPath);
        var matchedProcess = _processNameMatcher.FindMatchingProcess();
        var matchedProcessName = matchedProcess?.ProcessName;
        var projectPathMismatch = sessionInspection is not null &&
            sessionInspection.HasProjectPath &&
            !sessionInspection.MatchesProject;
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
                DetectionKind = CodexProcessDetectionKind.None,
                ActivityReason = "Codex process, window, and recent session activity were not detected."
            };
        }

        if (projectPathMismatch)
        {
            return new CodexProcessSnapshot(true, matchedProcessName, false)
            {
                DetectedActivityKind = CodexActivityKind.Ready,
                ActivityProvenance = ActivityProvenance.Observed,
                Confidence = ActivityConfidence.Low,
                DetectionKind = matchedProcess?.DetectionKind ?? CodexProcessDetectionKind.SessionActivity,
                ActivityReason = $"session project path '{sessionInspection?.ProjectPath}' does not match active project path '{projectPath}'",
                CollaborationMode = sessionInspection?.CollaborationMode,
                LastTaskStartedAt = sessionInspection?.LastTaskStartedAt,
                LastObservedAt = sessionInspection?.LastObservedAt,
                ObservedProjectPath = sessionInspection?.ProjectPath,
                RecentEditedFiles = Array.Empty<RecentProjectFileSnapshot>()
            };
        }

        var recentEditedFiles = _recentEditedFileTracker.GetRecentEditedFiles(projectSnapshot);
        var changedFileCount = gitSnapshot?.ChangedFileCount ?? 0;
        var context = new CodexActivityContext(
            recentEditedFiles,
            changedFileCount,
            sessionInspection,
            gitSnapshot,
            previousActivityKind,
            _presenceOptions.ThinkingStaleTimeoutMinutes,
            _presenceOptions.EditingFreshnessSeconds);

        var activity = _activityResolver.Resolve(
            context,
            out var provenance,
            out var confidence,
            out var reason,
            out var lastObservedAt);

        return new CodexProcessSnapshot(true, matchedProcessName, activity.IsActive())
        {
            DetectedActivityKind = activity,
            ActivityProvenance = provenance,
            Confidence = confidence,
            DetectionKind = matchedProcess?.DetectionKind ?? CodexProcessDetectionKind.SessionActivity,
            ActivityReason = reason,
            CollaborationMode = sessionInspection?.CollaborationMode,
            LastTaskStartedAt = sessionInspection?.LastTaskStartedAt,
            LastObservedAt = lastObservedAt,
            ObservedProjectPath = sessionInspection?.ProjectPath,
            RecentEditedFiles = recentEditedFiles
        };
    }

    public string? GetObservedProjectPath(string? projectPath = null)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var inspection = InspectRecentSessions(projectPath);
            if (inspection is not null &&
                inspection.MatchesProject &&
                !string.IsNullOrWhiteSpace(inspection.ProjectPath))
            {
                return inspection.ProjectPath;
            }

            return null;
        }

        return _sessionLogParser.GetLatestObservedProjectPath()
            ?? InspectRecentSessions(projectPath)?.ProjectPath;
    }

    public bool DetermineIfThinking(string? projectPath = null)
    {
        return GetSnapshot(projectPath).IsThinking;
    }

    private SessionInspection? InspectRecentSessions(string? projectPath)
    {
        return _sessionLogParser.InspectRecentSessions(projectPath);
    }
}
