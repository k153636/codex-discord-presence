namespace CodexDiscordPresence;

internal sealed record AntigravityPresenceProjectionResult(
    AntigravityActivitySnapshot Activity,
    string ModelName,
    string? ModelReasoningLevel,
    string? ModelVariant,
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
        var modelDisplay = ResolveModelDisplay(observation.Model);

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
            modelDisplay.ModelName,
            modelDisplay.ReasoningLevel,
            modelDisplay.Variant,
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
                    null),
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

    private static ModelDisplay ResolveModelDisplay(ProviderModelObservation? model)
    {
        var modelId = NormalizeDisplayValue(model?.Id);
        var displayName = NormalizeDisplayValue(model?.DisplayName);

        if (IsGeminiModel(modelId) || IsGeminiModel(displayName))
        {
            var reasoningLevel = NormalizeGeminiReasoningLevel(modelId) ??
                NormalizeGeminiReasoningLevel(displayName);
            return new(
                ResolveGeminiModelName(modelId, displayName, reasoningLevel),
                reasoningLevel,
                null);
        }

        var variant = NormalizeModelVariant(displayName) ?? NormalizeModelVariant(modelId);
        if (variant is null)
        {
            var modelName = displayName ?? modelId ?? FallbackModelName;
            return new(CodexModelDisplayFormatter.Format(modelName, null, null), null, null);
        }

        return new(
            ResolveVariantModelName(modelId, displayName, variant),
            null,
            variant);
    }

    private static bool IsGeminiModel(string? value)
    {
        return value is not null &&
            (string.Equals(value, "gemini", StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase) ||
             value.StartsWith("gemini ", StringComparison.OrdinalIgnoreCase));
    }

    private static string ResolveGeminiModelName(
        string? modelId,
        string? displayName,
        string? reasoningLevel)
    {
        if (IsGeminiModel(displayName))
        {
            if (displayName!.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase))
            {
                return FormatGeminiIdentifier(displayName, reasoningLevel);
            }

            return FormatGeminiIdentifier(
                RemoveTrailingGeminiReasoningLevel(displayName),
                reasoningLevel: null);
        }

        return modelId is not null
            ? FormatGeminiIdentifier(modelId, reasoningLevel)
            : displayName ?? FallbackModelName;
    }

    private static string? NormalizeGeminiReasoningLevel(string? value)
    {
        var lastToken = value is null
            ? null
            : TokenizeModel(value).LastOrDefault();
        return lastToken switch
        {
            "low" => "low",
            "medium" => "medium",
            "high" => "high",
            _ => null
        };
    }

    private static string RemoveTrailingGeminiReasoningLevel(string displayName)
    {
        var reasoningLevel = NormalizeGeminiReasoningLevel(displayName);
        if (reasoningLevel is null)
        {
            return displayName;
        }

        var parenthesizedSuffix = $"({reasoningLevel})";
        if (displayName.EndsWith(parenthesizedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return displayName[..^parenthesizedSuffix.Length].TrimEnd();
        }

        return displayName[..^reasoningLevel.Length].TrimEnd().TrimEnd('(').TrimEnd();
    }

    private static string FormatGeminiIdentifier(
        string modelName,
        string? reasoningLevel)
    {
        var tokens = TokenizeModel(modelName).ToArray();
        if (reasoningLevel is not null &&
            tokens.Length > 0 &&
            string.Equals(tokens[^1], reasoningLevel, StringComparison.OrdinalIgnoreCase))
        {
            tokens = tokens[..^1];
        }

        return string.Join(' ', tokens);
    }

    private static string ResolveVariantModelName(
        string? modelId,
        string? displayName,
        string variant)
    {
        var displayVariant = NormalizeModelVariant(displayName);
        var modelName = displayName ?? modelId ?? FallbackModelName;
        if (displayVariant is not null)
        {
            modelName = RemoveTrailingModelVariant(modelName, displayVariant);
        }
        else if (displayName is null && modelId is not null)
        {
            modelName = RemoveTrailingModelVariant(modelId, variant);
        }

        return string.Join(' ', TokenizeModel(modelName));
    }

    private static string? NormalizeModelVariant(string? value)
    {
        if (value is null)
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.EndsWith(")", StringComparison.Ordinal))
        {
            var openParenthesis = trimmed.LastIndexOf('(');
            if (openParenthesis >= 0 && openParenthesis < trimmed.Length - 2)
            {
                var parenthesizedValue = trimmed[(openParenthesis + 1)..^1];
                return string.Join(' ', TokenizeModel(parenthesizedValue));
            }
        }

        var lastToken = TokenizeModel(trimmed).LastOrDefault();
        return string.Equals(lastToken, "thinking", StringComparison.Ordinal)
            ? lastToken
            : null;
    }

    private static string RemoveTrailingModelVariant(string modelName, string variant)
    {
        var parenthesizedSuffix = $"({variant})";
        if (modelName.EndsWith(parenthesizedSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return modelName[..^parenthesizedSuffix.Length].TrimEnd();
        }

        return modelName[..^variant.Length].TrimEnd().TrimEnd('(').TrimEnd();
    }

    private static IEnumerable<string> TokenizeModel(string value)
    {
        return value
            .Replace('-', ' ')
            .Replace('_', ' ')
            .Replace('(', ' ')
            .Replace(')', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.ToLowerInvariant());
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

    private sealed record ModelDisplay(
        string ModelName,
        string? ReasoningLevel,
        string? Variant);
}
