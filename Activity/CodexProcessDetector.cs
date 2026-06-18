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
        _sessionLogParser = new CodexSessionLogParser(options);
    }

    public CodexProcessSnapshot GetSnapshot(
        string? projectPath = null,
        ProjectSnapshot? projectSnapshot = null,
        GitSnapshot? gitSnapshot = null,
        CodexActivityKind? previousActivityKind = null)
    {
        var sessionInspection = InspectRecentSessions(projectPath);
        var matchedProcessName = _processNameMatcher.FindMatchingProcessName();
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

    private SessionInspection? InspectRecentSessions(string? projectPath)
    {
        return _sessionLogParser.InspectRecentSessions(projectPath);
    }
}
