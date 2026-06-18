namespace CodexDiscordPresence;

internal sealed class CodexActivityResolver
{
    public CodexActivityKind Resolve(
        CodexActivityContext context,
        out ActivityProvenance provenance,
        out ActivityConfidence confidence,
        out string reason,
        out DateTime? lastObservedAt)
    {
        var recentEditedFiles = context.RecentEditedFiles;
        var changedFileCount = context.ChangedFileCount;
        var sessionInspection = context.SessionInspection;
        var gitSnapshot = context.GitSnapshot;
        var previousActivityKind = context.PreviousActivityKind;

        lastObservedAt = MaxTimestamp(
            sessionInspection?.LastObservedAt,
            recentEditedFiles.FirstOrDefault()?.LastWriteTimeUtc);

        var createdFileCount = gitSnapshot?.CreatedFileCount ?? 0;
        var deletedFileCount = gitSnapshot?.DeletedFileCount ?? 0;
        var hasFreshSession = sessionInspection is not null &&
            sessionInspection.HasRecentActivity(context.ThinkingStaleTimeoutMinutes);
        var hasFreshRecentEdits = CodexActivityEvidence.HasFreshRecentEdits(recentEditedFiles, context.EditingFreshnessSeconds);
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
                reason = $"recent edits={recentEditedFiles.Count}, git changed files={changedFileCount}";
                return CodexActivityKind.CoordinatingChanges;
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
            return CodexActivityKind.ApplyingEdits;
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
}
