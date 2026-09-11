namespace CodexDiscordPresence;

internal sealed record AntigravityPresenceProjectionResult(
    CodexProcessSnapshot Snapshot,
    string ModelName,
    string? WorkspaceName,
    string? ConversationId,
    string ActivityLine);

internal static class AntigravityPresenceProjection
{
    private const string FallbackModelName = "Unknown model";
    private const string FallbackActivityLine = "Idling";
    private const int MaxDisplayValueLength = 128;

    public static AntigravityPresenceProjectionResult Build(ProviderObservation observation)
    {
        return Build(observation, activeProjectPath: null);
    }

    public static AntigravityPresenceProjectionResult Build(
        ProviderObservation observation,
        string? activeProjectPath)
    {
        ArgumentNullException.ThrowIfNull(observation);
        _ = activeProjectPath;

        var observedAtUtc = ToUtcDateTime(observation.ObservedAtUtc);
        var state = MapState(observation.AgentState);
        var activityLine = GetActivityLine(observation.AgentState);
        var conversationId = NormalizeIdentifier(observation.ConversationId);

        var snapshot = new CodexProcessSnapshot(
            IsRunning: true,
            ProcessName: null,
            IsThinking: state.IsThinking)
        {
            DetectedActivityKind = state.ActivityKind,
            Confidence = ActivityConfidence.High,
            ActivityProvenance = ActivityProvenance.Observed,
            ActivityReason = "Provider status observation",
            LastObservedAt = observedAtUtc,
            ActivityStartedAt = observedAtUtc,
            LastEffectiveSignalAt = observedAtUtc,
            ActiveTurnId = conversationId,
            DetectionKind = CodexProcessDetectionKind.SessionActivity
        };

        return new AntigravityPresenceProjectionResult(
            snapshot,
            ResolveModelName(observation.Model),
            observation.Workspace?.WorkspaceName,
            conversationId,
            activityLine);
    }

    private static (CodexActivityKind ActivityKind, bool IsThinking) MapState(
        ProviderAgentState agentState)
    {
        return agentState switch
        {
            ProviderAgentState.Thinking => (CodexActivityKind.AnalyzingProject, true),
            ProviderAgentState.Working => (CodexActivityKind.ApplyingEdits, false),
            ProviderAgentState.ToolUse => (CodexActivityKind.RunningCommand, false),
            ProviderAgentState.Initializing => (CodexActivityKind.AnalyzingProject, true),
            ProviderAgentState.Idle => (CodexActivityKind.Ready, false),
            _ => (CodexActivityKind.Ready, false)
        };
    }

    private static string GetActivityLine(ProviderAgentState agentState)
    {
        return agentState switch
        {
            ProviderAgentState.Thinking => "Thinking",
            ProviderAgentState.Working => "Working",
            ProviderAgentState.ToolUse => "Using tools",
            ProviderAgentState.Initializing => "Starting",
            _ => FallbackActivityLine
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
