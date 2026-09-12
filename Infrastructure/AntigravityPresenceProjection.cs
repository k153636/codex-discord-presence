namespace CodexDiscordPresence;

internal sealed record AntigravityPresenceProjectionResult(
    AntigravityActivitySnapshot Activity,
    string ModelName,
    string? WorkspaceName,
    string? ConversationId,
    string? ExecutionMode,
    long? TotalTokens);

internal sealed record AntigravityActivitySnapshot(
    bool IsRunning,
    bool IsThinking,
    CodexActivityKind ActivityKind,
    string ProviderState,
    DateTime? ActivityStartedAt,
    DateTime? LastObservedAt,
    DateTime? LastEffectiveSignalAt,
    string? ActiveTurnId) : IPresenceActivitySnapshot
{
    public string? ProcessName => null;
    public ActivityConfidence Confidence => ActivityConfidence.High;
    public ActivityProvenance ActivityProvenance => ActivityProvenance.Observed;
    public string ActivityReason { get; init; } = "Antigravity CLI status-line agent state";
    public string? CollaborationMode => null;
    public DateTime? LastTaskStartedAt => null;
    public RunningCommandKind RunningCommandKind { get; init; }
    public string RunningCommandName { get; init; } = "";
    public string? LastDirectToolFilePath { get; init; }
    public DateTime? LastDirectToolFileAt { get; init; }
    public string? ActiveToolFilePath { get; init; }
    public bool IsMcpOperation => false;
    public string? McpServerName => null;
    public IReadOnlyList<string> ActiveMcpServerNames => Array.Empty<string>();
    public CodexActivityEventKind? LatestActivityEventKind { get; init; }
    public IReadOnlyList<string> ActivityFilePaths { get; init; } = Array.Empty<string>();
    public string? ActiveActivityDescription { get; init; }
    public int PendingOperationCount { get; init; }
    public int PendingMutationCount => 0;
    public string? LatestThinkingSummary => null;
    public int? PartySize => ActiveSubagentCount is > 0
        ? 1 + ActiveSubagentCount.Value
        : null;
    public int? ActiveSubagentCount { get; init; }
    public bool IsSuccessfulCompletion => false;
    public bool IsError => false;
    public bool HasDirectActivityEvidence => true;
    public IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles => Array.Empty<RecentProjectFileSnapshot>();
    public int ActivityRepeatCount => 1;
}

internal static class AntigravityPresenceProjection
{
    private const string FallbackModelName = "Unknown model";
    private const int MaxDisplayValueLength = 128;

    public static AntigravityPresenceProjectionResult Build(
        ProviderObservation observation,
        AntigravityActivitySnapshot? previousActivity = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var observedAtUtc = ToUtcDateTime(observation.ObservedAtUtc);
        var state = MapState(observation);
        var conversationId = NormalizeIdentifier(observation.ConversationId);
        var operation = observation.Operation is null
            ? null
            : AntigravityOperationClassifier.Classify(observation.Operation);
        var activeFilePath = NormalizeActivityFilePath(operation?.TargetPath);
        var activityFiles = activeFilePath is null
            ? Array.Empty<string>()
            : new[] { activeFilePath };
        var activityDescription = ResolveActivityDescription(state.ActivityKind, operation, activeFilePath);

        var activity = new AntigravityActivitySnapshot(
            IsRunning: true,
            IsThinking: state.IsThinking,
            ActivityKind: state.ActivityKind,
            ProviderState: ToProviderState(observation.AgentState),
            ActivityStartedAt: ResolveActivityStartedAt(
                observedAtUtc,
                state.ActivityKind,
                observation.AgentState,
                previousActivity),
            LastObservedAt: observedAtUtc,
            LastEffectiveSignalAt: observedAtUtc,
            ActiveTurnId: conversationId)
        {
            LatestActivityEventKind = state.LatestActivityEventKind,
            ActiveSubagentCount = NormalizeActiveSubagentCount(observation.ActiveSubagentCount),
            ActivityReason = ResolveActivityReason(observation, operation),
            RunningCommandKind = ResolveRunningCommandKind(operation),
            RunningCommandName = ResolveRunningCommandName(operation),
            LastDirectToolFilePath = activeFilePath,
            LastDirectToolFileAt = activeFilePath is null ? null : observedAtUtc,
            ActiveToolFilePath = activeFilePath,
            ActivityFilePaths = activityFiles,
            ActiveActivityDescription = activityDescription,
            PendingOperationCount = operation is not null && state.ActivityKind.IsActive() ? 1 : 0
        };

        return new AntigravityPresenceProjectionResult(
            activity,
            ResolveModelName(observation.Model),
            observation.Workspace?.WorkspaceName,
            conversationId,
            FormatExecutionMode(observation.ExecutionMode),
            NormalizeTotalTokens(observation.ContextWindow?.TotalTokens));
    }

    private static string ToProviderState(ProviderAgentState agentState)
    {
        return agentState switch
        {
            ProviderAgentState.Idle => "idle",
            ProviderAgentState.Thinking => "thinking",
            ProviderAgentState.Working => "working",
            ProviderAgentState.ToolUse => "tool_use",
            ProviderAgentState.Initializing => "initializing",
            _ => "unknown"
        };
    }

    private static StateMapping MapState(ProviderObservation observation)
    {
        if (observation.IsWaitingForInput)
        {
            return new(
                CodexActivityKind.WaitingForInput,
                false,
                CodexActivityEventKind.InputRequested);
        }

        var operation = observation.Operation is null
            ? null
            : AntigravityOperationClassifier.Classify(observation.Operation);
        if (operation is not null &&
            IsOperationState(observation.AgentState) &&
            (!operation.IsCompleted || observation.AgentState == ProviderAgentState.ToolUse))
        {
            var operationKind = operation.Kind switch
            {
                CodexOperationKind.Read => CodexActivityKind.ReadingFiles,
                CodexOperationKind.Edit => CodexActivityKind.ApplyingEdits,
                CodexOperationKind.Create => CodexActivityKind.CreatingFiles,
                CodexOperationKind.Delete => CodexActivityKind.DeletingFiles,
                CodexOperationKind.Research => CodexActivityKind.Researching,
                CodexOperationKind.Command => CodexActivityKind.RunningCommand,
                _ => CodexActivityKind.RunningCommand
            };

            return new(operationKind, false, CodexActivityEventKind.OperationStarted);
        }

        return observation.AgentState switch
        {
            ProviderAgentState.Thinking =>
                new(
                    observation.ExecutionMode == ProviderExecutionMode.Planning
                        ? CodexActivityKind.Planning
                        : CodexActivityKind.AnalyzingProject,
                    true,
                    CodexActivityEventKind.Reasoning),
            ProviderAgentState.Working =>
                new(
                    observation.ExecutionMode == ProviderExecutionMode.Planning
                        ? CodexActivityKind.Planning
                        : CodexActivityKind.AnalyzingProject,
                    true,
                    CodexActivityEventKind.Reasoning),
            ProviderAgentState.ToolUse =>
                new(CodexActivityKind.RunningCommand, false, CodexActivityEventKind.OperationStarted),
            ProviderAgentState.Initializing =>
                observation.ExecutionMode == ProviderExecutionMode.Planning
                    ? new(CodexActivityKind.Planning, true, CodexActivityEventKind.TurnStarted)
                    : new(CodexActivityKind.AnalyzingProject, true, CodexActivityEventKind.TurnStarted),
            ProviderAgentState.Idle =>
                new(CodexActivityKind.Ready, false, CodexActivityEventKind.TurnCompleted),
            _ =>
                new(CodexActivityKind.Ready, false, null)
        };
    }

    private static bool IsOperationState(ProviderAgentState agentState) =>
        agentState is ProviderAgentState.Thinking or
            ProviderAgentState.Working or
            ProviderAgentState.ToolUse;

    private static string ResolveActivityReason(
        ProviderObservation observation,
        ProviderOperationObservation? operation)
    {
        if (observation.IsWaitingForInput)
        {
            return "Antigravity CLI confirmation pending";
        }

        return operation?.ToolName is { Length: > 0 } toolName
            ? $"Antigravity tool {toolName}"
            : "Antigravity CLI status-line agent state";
    }

    private static string? ResolveActivityDescription(
        CodexActivityKind activityKind,
        ProviderOperationObservation? operation,
        string? activeFilePath)
    {
        if (operation is null || activityKind is not (CodexActivityKind.ReadingFiles or CodexActivityKind.Researching))
        {
            return null;
        }

        var description = NormalizeDisplayValue(operation.Action ?? operation.Summary);
        if (description is not null)
        {
            return description;
        }

        return activeFilePath is null
            ? null
            : $"{(activityKind == CodexActivityKind.Researching ? "Researching" : "Reading")} {Path.GetFileName(activeFilePath)}";
    }

    private static RunningCommandKind ResolveRunningCommandKind(ProviderOperationObservation? operation)
    {
        if (operation?.Kind != CodexOperationKind.Command)
        {
            return RunningCommandKind.Unknown;
        }

        var text = string.Join(
            ' ',
            operation.ToolName,
            operation.Action,
            operation.Summary).ToLowerInvariant();
        return text switch
        {
            _ when text.Contains("git", StringComparison.Ordinal) => RunningCommandKind.Git,
            _ when text.Contains("test", StringComparison.Ordinal) => RunningCommandKind.Test,
            _ when text.Contains("build", StringComparison.Ordinal) => RunningCommandKind.Build,
            _ when text.Contains("search", StringComparison.Ordinal) || text.Contains("grep", StringComparison.Ordinal) => RunningCommandKind.Search,
            _ => RunningCommandKind.Unknown
        };
    }

    private static string ResolveRunningCommandName(ProviderOperationObservation? operation)
    {
        if (operation?.Kind != CodexOperationKind.Command)
        {
            return "";
        }

        return NormalizeDisplayValue(operation.Action ?? operation.Summary ?? operation.ToolName) ?? "";
    }

    private static string? NormalizeActivityFilePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
        {
            return null;
        }

        try
        {
            return Path.IsPathFullyQualified(path)
                ? Path.GetFullPath(path)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static DateTime? ResolveActivityStartedAt(
        DateTime? observedAtUtc,
        CodexActivityKind activityKind,
        ProviderAgentState agentState,
        AntigravityActivitySnapshot? previousActivity)
    {
        if (!observedAtUtc.HasValue)
        {
            return null;
        }

        return previousActivity is not null &&
            previousActivity.ActivityKind == activityKind &&
            string.Equals(
                previousActivity.ProviderState,
                ToProviderState(agentState),
                StringComparison.Ordinal) &&
            previousActivity.ActivityStartedAt.HasValue
            ? previousActivity.ActivityStartedAt
            : observedAtUtc;
    }

    private static string? FormatExecutionMode(ProviderExecutionMode executionMode)
    {
        return executionMode switch
        {
            ProviderExecutionMode.Planning => "Planning",
            ProviderExecutionMode.Fast => "Fast",
            _ => null
        };
    }

    private static int? NormalizeActiveSubagentCount(int? count) =>
        count is > 0 and <= ProviderObservation.MaxActiveSubagentCount ? count : null;

    private static string ResolveModelName(ProviderModelObservation? model)
    {
        var modelName = NormalizeDisplayValue(model?.DisplayName) ??
            NormalizeDisplayValue(model?.Id) ??
            FallbackModelName;
        return CodexModelDisplayFormatter.Format(modelName, null, null);
    }

    private static long? NormalizeTotalTokens(long? totalTokens) =>
        totalTokens is > 0 ? totalTokens : null;

    private static DateTime? ToUtcDateTime(DateTimeOffset observedAtUtc)
    {
        return observedAtUtc == default
            ? null
            : observedAtUtc.UtcDateTime;
    }

    private static string? NormalizeIdentifier(string? value)
    {
        var normalized = NormalizeDisplayValue(value);
        return normalized is not null &&
            !normalized.Contains('/', StringComparison.Ordinal) &&
            !normalized.Contains('\\', StringComparison.Ordinal)
            ? normalized
            : null;
    }

    private static string? NormalizeDisplayValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = string.Join(
            ' ',
            new string(value.Trim().Where(character => !char.IsControl(character)).ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0 || LooksLikeAbsolutePath(normalized))
        {
            return null;
        }

        if (normalized.Length <= MaxDisplayValueLength)
        {
            return normalized;
        }

        var truncated = normalized[..MaxDisplayValueLength];
        return char.IsHighSurrogate(truncated[^1])
            ? truncated[..^1]
            : truncated;
    }

    private static bool LooksLikeAbsolutePath(string value)
    {
        return value.Contains('\\') ||
            (value.Length >= 3 && char.IsLetter(value[0]) && value[1] == ':' && value[2] == '/') ||
            value.StartsWith("/", StringComparison.Ordinal);
    }

    private sealed record StateMapping(
        CodexActivityKind ActivityKind,
        bool IsThinking,
        CodexActivityEventKind? LatestActivityEventKind);
}
