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
    public string ActivityReason => "Antigravity CLI status-line agent state";
    public string? CollaborationMode => null;
    public DateTime? LastTaskStartedAt => null;
    public RunningCommandKind RunningCommandKind => RunningCommandKind.Unknown;
    public string RunningCommandName => "";
    public string? LastDirectToolFilePath => null;
    public DateTime? LastDirectToolFileAt => null;
    public string? ActiveToolFilePath => null;
    public bool IsMcpOperation => false;
    public string? McpServerName => null;
    public IReadOnlyList<string> ActiveMcpServerNames => Array.Empty<string>();
    public CodexActivityEventKind? LatestActivityEventKind { get; init; }
    public IReadOnlyList<string> ActivityFilePaths => Array.Empty<string>();
    public int PendingOperationCount => 0;
    public int PendingMutationCount => 0;
    public string? LatestThinkingSummary => null;
    public int? PartySize => 1;
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
        var state = MapState(observation.AgentState);
        var conversationId = NormalizeIdentifier(observation.ConversationId);

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
            LatestActivityEventKind = state.LatestActivityEventKind
        };

        return new AntigravityPresenceProjectionResult(
            activity,
            ResolveModelName(observation.Model),
            observation.Workspace?.WorkspaceName,
            conversationId,
            FormatExecutionMode(observation.ExecutionMode),
            observation.ContextWindow?.TotalTokens);
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

    private static (CodexActivityKind ActivityKind, bool IsThinking, CodexActivityEventKind? LatestActivityEventKind) MapState(
        ProviderAgentState agentState)
    {
        return agentState switch
        {
            ProviderAgentState.Thinking =>
                (CodexActivityKind.AnalyzingProject, true, CodexActivityEventKind.Reasoning),
            ProviderAgentState.Working =>
                (CodexActivityKind.AnalyzingProject, true, null),
            ProviderAgentState.ToolUse =>
                (CodexActivityKind.RunningCommand, false, CodexActivityEventKind.OperationStarted),
            ProviderAgentState.Initializing =>
                (CodexActivityKind.AnalyzingProject, true, CodexActivityEventKind.TurnStarted),
            ProviderAgentState.Idle =>
                (CodexActivityKind.Ready, false, CodexActivityEventKind.TurnCompleted),
            _ =>
                (CodexActivityKind.Ready, false, null)
        };
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

    private static string ResolveModelName(ProviderModelObservation? model)
    {
        return NormalizeDisplayValue(model?.DisplayName) ??
            NormalizeDisplayValue(model?.Id) ??
            FallbackModelName;
    }

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
}
