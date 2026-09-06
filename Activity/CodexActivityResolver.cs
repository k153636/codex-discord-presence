namespace CodexDiscordPresence;

internal sealed class CodexActivityResolver
{
    private readonly CodexActivityStateMachine _stateMachine;
    private readonly Func<DateTime> _utcNow;

    public CodexActivityResolver()
        : this(null, null)
    {
    }

    internal CodexActivityResolver(
        CodexActivityStateMachine? stateMachine,
        Func<DateTime>? utcNow)
    {
        _stateMachine = stateMachine ?? new CodexActivityStateMachine();
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public CodexActivityKind Resolve(
        CodexActivityContext context,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason,
        out DateTime? lastObservedAt)
    {
        return Resolve(
            context,
            out provenance,
            out confidence,
            out reason,
            out _,
            out lastObservedAt);
    }

    internal CodexActivityKind Resolve(
        CodexActivityContext context,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason,
        out CodexActivityState? activityState,
        out DateTime? lastObservedAt)
    {
        var nowUtc = _utcNow();
        var recentEditedFiles = context.RecentEditedFiles;
        var changedFileCount = context.ChangedFileCount;
        var sessionInspection = context.SessionInspection;
        var gitSnapshot = context.GitSnapshot;
        var previousActivityKind = context.PreviousActivityKind;

        activityState = ResolveActivityState(sessionInspection, nowUtc);
        lastObservedAt = activityState is not null
            ? MaxTimestamp(activityState.LastEventAtUtc, activityState.LastEffectiveSignalAtUtc)
            : MaxTimestamp(
                sessionInspection?.LastObservedAt,
                recentEditedFiles.FirstOrDefault()?.LastWriteTimeUtc);

        if (activityState is not null)
        {
            return ResolveEventState(
                activityState,
                sessionInspection,
                out provenance,
                out confidence,
                out reason);
        }

        var createdFileCount = gitSnapshot?.CreatedFileCount ?? 0;
        var deletedFileCount = gitSnapshot?.DeletedFileCount ?? 0;
        var hasFreshSession = sessionInspection is not null &&
            sessionInspection.HasRecentActivity(context.ThinkingStaleTimeoutMinutes);
        var hasRecentShellCommandActivity = sessionInspection is not null &&
            sessionInspection.LastRunningCommandKind != RunningCommandKind.Unknown &&
            sessionInspection.LastShellCommandAt.HasValue &&
            nowUtc - sessionInspection.LastShellCommandAt.Value <= TimeSpan.FromSeconds(Math.Max(0, context.RunningCommandHoldSeconds));
        var hasRecentTaskStarted = sessionInspection is not null &&
            sessionInspection.HasTaskStarted &&
            sessionInspection.LastTaskStartedAt.HasValue &&
            nowUtc - sessionInspection.LastTaskStartedAt.Value <= TimeSpan.FromMinutes(Math.Max(0, context.ThinkingStaleTimeoutMinutes));
        var hasFreshRecentEdits = CodexActivityEvidence.HasFreshRecentEdits(recentEditedFiles, context.EditingFreshnessSeconds);
        var hasBurstRecentEdits = CodexActivityEvidence.HasBurstRecentEdits(recentEditedFiles, changedFileCount);
        var hasRefactorEvidence = CodexActivityEvidence.HasRefactorEvidence(gitSnapshot);
        var hasSingleCreatingEvidence = hasFreshSession &&
            hasFreshRecentEdits &&
            recentEditedFiles.Count == 1 &&
            createdFileCount > 0 &&
            deletedFileCount == 0 &&
            changedFileCount == 1 &&
            createdFileCount == 1;
        var hasDeletingEvidence = hasFreshSession &&
            deletedFileCount > 0 &&
            createdFileCount == 0 &&
            changedFileCount == deletedFileCount;

        // A completed task is terminal until a newer task_started event appears.
        // Git changes and recent file timestamps can outlive the task that created
        // them, so they must not resurrect an active state after task_complete.
        if (sessionInspection?.HasTaskCompleted == true &&
            (!sessionInspection.HasTaskStarted || sessionInspection.HasTaskCompletedSinceStart))
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = sessionInspection.HasTaskStarted
                ? "task_complete without newer task_started"
                : "task_complete without task_started";
            return CodexActivityKind.Ready;
        }

        if (hasFreshSession && (sessionInspection?.HasRunningCommand == true || hasRecentShellCommandActivity))
        {
            var runningCommandReason = sessionInspection?.RunningCommandReason;
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = runningCommandReason ?? "pending shell_command function call in session log";
            return CodexActivityKind.RunningCommand;
        }

        if (hasSingleCreatingEvidence)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            reason = $"created file={recentEditedFiles[0].Name}, git changed files={changedFileCount}";
            return CodexActivityKind.CreatingFiles;
        }

        if (hasFreshRecentEdits)
        {
            provenance = ActivityProvenance.Observed;
            confidence = ActivityConfidence.High;
            if (hasBurstRecentEdits)
            {
                reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
                return CodexActivityKind.CoordinatingChanges;
            }

            reason = $"recent edit={recentEditedFiles[0].Name}, git changed files={changedFileCount}";
            return CodexActivityKind.ApplyingEdits;
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
            return CodexActivityKind.ApplyingEdits;
        }

        if (hasFreshSession && hasRecentTaskStarted)
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
            reason = "Codex running but idle";
            return CodexActivityKind.Ready;
        }

        provenance = ActivityProvenance.Inferred;
        confidence = ActivityConfidence.High;
        reason = "Codex running but idle";
        return CodexActivityKind.Ready;
    }

    private CodexActivityState? ResolveActivityState(SessionInspection? sessionInspection, DateTime nowUtc)
    {
        if (sessionInspection is null ||
            !sessionInspection.ActivityEvents.Any(activityEvent => activityEvent.Kind != CodexActivityEventKind.ContextUpdated))
        {
            return null;
        }

        return _stateMachine.Evaluate(sessionInspection.ActivityEvents, nowUtc);
    }

    private static CodexActivityKind ResolveEventState(
        CodexActivityState state,
        SessionInspection? sessionInspection,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason)
    {
        provenance = state.Lifecycle == CodexTurnLifecycle.Stalled
            ? ActivityProvenance.Inferred
            : ActivityProvenance.Observed;
        confidence = state.Lifecycle == CodexTurnLifecycle.Stalled
            ? ActivityConfidence.Low
            : ActivityConfidence.High;
        reason = state.Reason;

        return state.Lifecycle switch
        {
            CodexTurnLifecycle.Completed => CodexActivityKind.Ready,
            CodexTurnLifecycle.Failed => CodexActivityKind.Ready,
            CodexTurnLifecycle.Interrupted => CodexActivityKind.Ready,
            CodexTurnLifecycle.WaitingForInput => CodexActivityKind.WaitingForInput,
            CodexTurnLifecycle.Stalled => CodexActivityKind.Stalled,
            CodexTurnLifecycle.Open => ResolveOpenEventState(state, sessionInspection),
            _ => CodexActivityKind.Ready
        };
    }

    private static CodexActivityKind ResolveOpenEventState(
        CodexActivityState state,
        SessionInspection? sessionInspection)
    {
        return state.OperationKind switch
        {
            CodexOperationKind.Edit => state.MutationFilePaths.Count > 1 && state.ActiveFilePath is null
                ? CodexActivityKind.CoordinatingChanges
                : CodexActivityKind.ApplyingEdits,
            CodexOperationKind.Create => CodexActivityKind.CreatingFiles,
            CodexOperationKind.Delete => CodexActivityKind.DeletingFiles,
            CodexOperationKind.Command => CodexActivityKind.RunningCommand,
            CodexOperationKind.Read => CodexActivityKind.ReadingFiles,
            _ when sessionInspection?.CollaborationMode is "plan" => CodexActivityKind.Planning,
            _ => CodexActivityKind.AnalyzingProject
        };
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

}
