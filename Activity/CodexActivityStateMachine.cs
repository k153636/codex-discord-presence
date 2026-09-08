namespace CodexDiscordPresence;

internal sealed record CodexActivityState
{
    public CodexTurnLifecycle Lifecycle { get; init; }
    public CodexOperationKind OperationKind { get; init; }
    public bool IsMcpOperation { get; init; }
    public string? McpServerName { get; init; }
    public IReadOnlyList<string> ActiveMcpServerNames { get; init; } = Array.Empty<string>();
    public string? LatestThinkingSummary { get; init; }
    public string? TurnId { get; init; }
    public string? ActiveFilePath { get; init; }
    public IReadOnlyList<string> MutationFilePaths { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> PendingTargetPaths { get; init; } = Array.Empty<string>();
    public int PendingOperationCount { get; init; }
    public int PendingMutationCount { get; init; }
    public RunningCommandKind CommandKind { get; init; } = RunningCommandKind.Unknown;
    public string CommandName { get; init; } = "";
    public DateTime? TurnStartedAtUtc { get; init; }
    public DateTime? LastEventAtUtc { get; init; }
    public DateTime? LastEffectiveSignalAtUtc { get; init; }
    public DateTime? TerminalAtUtc { get; init; }
    public CodexActivityEvent? TriggerEvent { get; init; }
    public CodexActivityEvent? ActiveOperationEvent { get; init; }
    public bool IsCompletedMutationDisplay { get; init; }
    public string Reason { get; init; } = "";
    public CodexActivitySource Source { get; init; } = CodexActivitySource.SessionLog;

    public bool HasMultipleMutationTargets => MutationFilePaths.Count > 1;
}

internal sealed class CodexActivityStateMachine
{
    private static readonly TimeSpan CompletedMutationDisplayGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CompletedMcpDisplayGrace = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan CompletedResearchDisplayGrace = TimeSpan.FromSeconds(2);
    private readonly TimeSpan _staleAfter;
    private readonly TimeSpan _reasoningGrace;

    public CodexActivityStateMachine(
        TimeSpan? staleAfter = null,
        TimeSpan? reasoningGrace = null)
    {
        _staleAfter = staleAfter ?? TimeSpan.FromSeconds(45);
        _reasoningGrace = reasoningGrace ?? TimeSpan.FromSeconds(4);
    }

    public CodexActivityState Evaluate(
        IEnumerable<CodexActivityEvent> events,
        DateTime nowUtc)
    {
        var orderedEvents = events
            .OrderBy(activityEvent => activityEvent.Sequence)
            .ThenBy(activityEvent => activityEvent.TimestampUtc)
            .ToArray();

        var currentTurnId = (string?)null;
        DateTime? turnStartedAtUtc = null;
        DateTime? lastEventAtUtc = null;
        DateTime? lastEffectiveSignalAtUtc = null;
        DateTime? terminalAtUtc = null;
        CodexActivityEvent? lastEffectiveEvent = null;
        CodexActivityEvent? terminalEvent = null;
        string? latestThinkingSummary = null;
        var lifecycle = CodexTurnLifecycle.None;
        var pendingOperations = new Dictionary<string, PendingOperation>(StringComparer.Ordinal);
        var pendingOperationsWithoutId = new List<PendingOperation>();
        var pendingInputs = new HashSet<string>(StringComparer.Ordinal);
        var mutationPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var operationSequence = 0L;
        var syntheticTurnSequence = 0L;
        PendingOperation? lastCompletedMutation = null;
        var lastCompletedMutationSequence = 0L;
        DateTime? lastCompletedMutationAtUtc = null;
        PendingOperation? lastCompletedMcpOperation = null;
        var lastCompletedMcpOperationSequence = 0L;
        DateTime? lastCompletedMcpOperationAtUtc = null;
        PendingOperation? lastCompletedResearchOperation = null;
        var lastCompletedResearchOperationSequence = 0L;
        DateTime? lastCompletedResearchOperationAtUtc = null;

        foreach (var activityEvent in orderedEvents)
        {
            if (activityEvent.Kind == CodexActivityEventKind.TurnStarted)
            {
                var nextTurnId = NormalizeTurnId(activityEvent.TurnId);
                if (nextTurnId is null)
                {
                    syntheticTurnSequence++;
                    nextTurnId = $"implicit:{syntheticTurnSequence}";
                }

                if (!string.Equals(currentTurnId, nextTurnId, StringComparison.Ordinal) ||
                    lifecycle is CodexTurnLifecycle.Completed or CodexTurnLifecycle.Failed or CodexTurnLifecycle.Interrupted)
                {
                    currentTurnId = nextTurnId;
                    turnStartedAtUtc = activityEvent.TimestampUtc;
                    terminalAtUtc = null;
                    terminalEvent = null;
                    lifecycle = CodexTurnLifecycle.Open;
                    pendingOperations.Clear();
                    pendingOperationsWithoutId.Clear();
                    pendingInputs.Clear();
                    mutationPaths.Clear();
                    latestThinkingSummary = null;
                    lastCompletedMcpOperation = null;
                    lastCompletedMcpOperationSequence = 0L;
                    lastCompletedMcpOperationAtUtc = null;
                    lastCompletedResearchOperation = null;
                    lastCompletedResearchOperationSequence = 0L;
                    lastCompletedResearchOperationAtUtc = null;
                }

                lastEventAtUtc = Max(lastEventAtUtc, activityEvent.TimestampUtc);
                lastEffectiveSignalAtUtc = activityEvent.TimestampUtc;
                lastEffectiveEvent = activityEvent;
                continue;
            }

            if (currentTurnId is null && activityEvent.Kind is
                CodexActivityEventKind.Reasoning or
                CodexActivityEventKind.OperationStarted or
                CodexActivityEventKind.InputRequested)
            {
                currentTurnId = NormalizeTurnId(activityEvent.TurnId) ?? $"implicit:{activityEvent.Sequence}";
                turnStartedAtUtc = activityEvent.TimestampUtc;
                lifecycle = CodexTurnLifecycle.Open;
            }

            if (currentTurnId is null)
            {
                continue;
            }

            if (!BelongsToCurrentTurn(activityEvent, currentTurnId))
            {
                continue;
            }

            if (lifecycle is CodexTurnLifecycle.Completed or CodexTurnLifecycle.Failed or CodexTurnLifecycle.Interrupted)
            {
                continue;
            }

            if (activityEvent.Kind == CodexActivityEventKind.Reasoning)
            {
                latestThinkingSummary = activityEvent.ThinkingSummary;
            }
            else if (!string.IsNullOrWhiteSpace(activityEvent.ThinkingSummary))
            {
                latestThinkingSummary = activityEvent.ThinkingSummary;
            }

            lastEventAtUtc = Max(lastEventAtUtc, activityEvent.TimestampUtc);
            if (activityEvent.IsEffective)
            {
                lastEffectiveSignalAtUtc = activityEvent.TimestampUtc;
                lastEffectiveEvent = activityEvent;
            }

            switch (activityEvent.Kind)
            {
                case CodexActivityEventKind.Reasoning:
                    lifecycle = CodexTurnLifecycle.Open;
                    break;

                case CodexActivityEventKind.OperationStarted:
                {
                    operationSequence++;
                    var operation = new PendingOperation(operationSequence, activityEvent);
                    if (string.IsNullOrWhiteSpace(activityEvent.CallId))
                    {
                        pendingOperationsWithoutId.Add(operation);
                    }
                    else
                    {
                        pendingOperations[activityEvent.CallId] = operation;
                    }

                    if (IsMutation(activityEvent.OperationKind))
                    {
                        foreach (var path in activityEvent.TargetPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
                        {
                            mutationPaths.Add(path);
                        }
                    }

                    lifecycle = CodexTurnLifecycle.Open;
                    break;
                }

                case CodexActivityEventKind.OperationCompleted:
                    if (IsMutation(activityEvent.OperationKind))
                    {
                        foreach (var path in activityEvent.TargetPaths.Where(path => !string.IsNullOrWhiteSpace(path)))
                        {
                            mutationPaths.Add(path);
                        }
                    }

                    var completedOperation = CompleteOperation(activityEvent, pendingOperations, pendingOperationsWithoutId);
                    var completedMcpEvent = activityEvent.IsMcpOperation
                        ? activityEvent
                        : completedOperation?.Event.IsMcpOperation == true
                            ? completedOperation.Event
                            : null;
                    if (completedMcpEvent is not null)
                    {
                        lastCompletedMcpOperation = new PendingOperation(activityEvent.Sequence, completedMcpEvent);
                        lastCompletedMcpOperationSequence = activityEvent.Sequence;
                        lastCompletedMcpOperationAtUtc = activityEvent.TimestampUtc;
                    }
                    if (completedOperation is not null && IsMutation(completedOperation.Event.OperationKind))
                    {
                        lastCompletedMutation = completedOperation;
                        lastCompletedMutationSequence = activityEvent.Sequence;
                        lastCompletedMutationAtUtc = activityEvent.TimestampUtc;
                    }
                    var completedResearchEvent = activityEvent.OperationKind == CodexOperationKind.Research
                        ? activityEvent
                        : completedOperation?.Event.OperationKind == CodexOperationKind.Research
                            ? completedOperation.Event
                            : null;
                    if (completedResearchEvent is not null)
                    {
                        lastCompletedResearchOperation = new PendingOperation(activityEvent.Sequence, completedResearchEvent);
                        lastCompletedResearchOperationSequence = activityEvent.Sequence;
                        lastCompletedResearchOperationAtUtc = activityEvent.TimestampUtc;
                    }
                    if (!string.IsNullOrWhiteSpace(activityEvent.CallId))
                    {
                        pendingInputs.Remove(activityEvent.CallId);
                    }

                    lifecycle = CodexTurnLifecycle.Open;
                    break;

                case CodexActivityEventKind.InputRequested:
                {
                    var inputId = NormalizeInputId(activityEvent);
                    if (inputId is not null)
                    {
                        pendingInputs.Add(inputId);
                    }

                    lifecycle = CodexTurnLifecycle.WaitingForInput;
                    break;
                }

                case CodexActivityEventKind.InputResolved:
                {
                    var inputId = NormalizeInputId(activityEvent);
                    if (inputId is not null)
                    {
                        pendingInputs.Remove(inputId);
                    }

                    lifecycle = pendingInputs.Count > 0
                        ? CodexTurnLifecycle.WaitingForInput
                        : CodexTurnLifecycle.Open;
                    break;
                }

                case CodexActivityEventKind.TurnCompleted:
                    lifecycle = CodexTurnLifecycle.Completed;
                    terminalAtUtc = activityEvent.TimestampUtc;
                    terminalEvent = activityEvent;
                    pendingOperations.Clear();
                    pendingOperationsWithoutId.Clear();
                    pendingInputs.Clear();
                    break;

                case CodexActivityEventKind.TurnFailed:
                    lifecycle = CodexTurnLifecycle.Failed;
                    terminalAtUtc = activityEvent.TimestampUtc;
                    terminalEvent = activityEvent;
                    pendingOperations.Clear();
                    pendingOperationsWithoutId.Clear();
                    pendingInputs.Clear();
                    break;

                case CodexActivityEventKind.TurnInterrupted:
                    lifecycle = CodexTurnLifecycle.Interrupted;
                    terminalAtUtc = activityEvent.TimestampUtc;
                    terminalEvent = activityEvent;
                    pendingOperations.Clear();
                    pendingOperationsWithoutId.Clear();
                    pendingInputs.Clear();
                    break;
            }
        }

        if (currentTurnId is null)
        {
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.None,
                Reason = "no active turn",
                Source = lastEffectiveEvent?.Source ?? CodexActivitySource.SessionLog
            };
        }

        if (lifecycle is CodexTurnLifecycle.Completed or CodexTurnLifecycle.Failed or CodexTurnLifecycle.Interrupted)
        {
            return new CodexActivityState
            {
                Lifecycle = lifecycle,
                TurnId = currentTurnId,
                TurnStartedAtUtc = turnStartedAtUtc,
                LastEventAtUtc = lastEventAtUtc,
                LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
                TerminalAtUtc = terminalAtUtc,
                TriggerEvent = terminalEvent,
                MutationFilePaths = Array.Empty<string>(),
                Reason = terminalEvent?.Reason ?? $"turn {lifecycle.ToString().ToLowerInvariant()}"
            };
        }

        if (pendingInputs.Count > 0)
        {
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.WaitingForInput,
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                TurnStartedAtUtc = turnStartedAtUtc,
                LastEventAtUtc = lastEventAtUtc,
                LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
                PendingOperationCount = pendingOperations.Count + pendingOperationsWithoutId.Count,
                PendingMutationCount = CountPendingMutations(pendingOperations, pendingOperationsWithoutId),
                MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                TriggerEvent = lastEffectiveEvent,
                Reason = "input or permission request is unresolved",
                Source = terminalEvent?.Source ?? CodexActivitySource.SessionLog
            };
        }

        var pending = pendingOperations.Values
            .Concat(pendingOperationsWithoutId)
            .OrderByDescending(operation => operation.Sequence)
            .ToArray();
        if (pending.Length > 0)
        {
            var pendingEffectiveAge = lastEffectiveSignalAtUtc.HasValue
                ? nowUtc - lastEffectiveSignalAtUtc.Value
                : _staleAfter;
            if (pendingEffectiveAge >= _staleAfter)
            {
                return new CodexActivityState
                {
                    Lifecycle = CodexTurnLifecycle.Stalled,
                    TurnId = currentTurnId,
                    LatestThinkingSummary = latestThinkingSummary,
                    TurnStartedAtUtc = turnStartedAtUtc,
                    LastEventAtUtc = lastEventAtUtc,
                    LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
                    MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                    PendingTargetPaths = pending
                        .SelectMany(operation => operation.Event.TargetPaths)
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    PendingOperationCount = pending.Length,
                    PendingMutationCount = pending.Count(operation => IsMutation(operation.Event.OperationKind)),
                    IsMcpOperation = pending.Any(operation => operation.Event.IsMcpOperation),
                    McpServerName = pending
                        .Select(operation => operation.Event.McpServerName)
                        .FirstOrDefault(name => !string.IsNullOrWhiteSpace(name)),
                    ActiveMcpServerNames = GetActiveMcpServerNames(pending),
                    TriggerEvent = lastEffectiveEvent,
                    ActiveOperationEvent = pending[0].Event,
                    Reason = $"pending operation produced no completion for {Math.Max(0, (int)pendingEffectiveAge.TotalSeconds)} seconds",
                    Source = lastEffectiveEvent?.Source ?? CodexActivitySource.SessionLog
                };
            }

            var activeOperation = pending[0];
            var pendingTargetPaths = pending
                .SelectMany(operation => operation.Event.TargetPaths)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var activeFilePath = ResolveActiveFilePath(activeOperation, pending);

            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.Open,
                OperationKind = activeOperation.Event.OperationKind,
                IsMcpOperation = activeOperation.Event.IsMcpOperation,
                McpServerName = activeOperation.Event.McpServerName,
                ActiveMcpServerNames = GetActiveMcpServerNames(pending),
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                ActiveFilePath = activeFilePath,
                MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                PendingTargetPaths = pendingTargetPaths,
                PendingOperationCount = pending.Length,
                PendingMutationCount = pending.Count(operation => IsMutation(operation.Event.OperationKind)),
                CommandKind = activeOperation.Event.CommandKind,
                CommandName = activeOperation.Event.CommandName ?? "",
                TurnStartedAtUtc = turnStartedAtUtc,
                LastEventAtUtc = lastEventAtUtc,
                LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
                TriggerEvent = lastEffectiveEvent,
                ActiveOperationEvent = activeOperation.Event,
                Reason = activeOperation.Event.Reason ?? $"pending {activeOperation.Event.OperationKind.ToString().ToLowerInvariant()} operation",
                Source = activeOperation.Event.Source
            };
        }

        if (lastCompletedMcpOperation is not null &&
            lastEffectiveEvent?.Sequence == lastCompletedMcpOperationSequence &&
            lastCompletedMcpOperationAtUtc.HasValue &&
            nowUtc - lastCompletedMcpOperationAtUtc.Value <= CompletedMcpDisplayGrace)
        {
            var completedMcp = lastCompletedMcpOperation.Event;
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.Open,
                OperationKind = completedMcp.OperationKind,
                IsMcpOperation = true,
                McpServerName = completedMcp.McpServerName,
                ActiveMcpServerNames = string.IsNullOrWhiteSpace(completedMcp.McpServerName)
                    ? Array.Empty<string>()
                    : [completedMcp.McpServerName],
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                PendingOperationCount = 0,
                PendingMutationCount = 0,
                TriggerEvent = lastEffectiveEvent,
                ActiveOperationEvent = completedMcp,
                Reason = "MCP operation completed; waiting for next Codex event",
                Source = completedMcp.Source
            };
        }

        if (lastCompletedMutation is not null &&
            lastEffectiveEvent?.Sequence == lastCompletedMutationSequence &&
            lastCompletedMutationAtUtc.HasValue &&
            nowUtc - lastCompletedMutationAtUtc.Value <= CompletedMutationDisplayGrace)
        {
            var completedMutation = lastCompletedMutation.Event;
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.Open,
                OperationKind = completedMutation.OperationKind,
                IsMcpOperation = completedMutation.IsMcpOperation,
                McpServerName = completedMutation.McpServerName,
                ActiveMcpServerNames = completedMutation.IsMcpOperation &&
                    !string.IsNullOrWhiteSpace(completedMutation.McpServerName)
                    ? [completedMutation.McpServerName]
                    : Array.Empty<string>(),
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                ActiveFilePath = ResolveActiveFilePath(lastCompletedMutation, [lastCompletedMutation]),
                MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                PendingTargetPaths = completedMutation.TargetPaths,
                PendingOperationCount = 0,
                PendingMutationCount = 0,
                TriggerEvent = lastEffectiveEvent,
                ActiveOperationEvent = completedMutation,
                IsCompletedMutationDisplay = true,
                Reason = "mutation completed; waiting for next Codex event",
                Source = completedMutation.Source
            };
        }

        if (lastCompletedResearchOperation is not null &&
            IsCompletedResearchStillCurrent(
                lastCompletedResearchOperation,
                lastCompletedResearchOperationSequence,
                lastEffectiveEvent) &&
            lastCompletedResearchOperationAtUtc.HasValue &&
            nowUtc - lastCompletedResearchOperationAtUtc.Value <= CompletedResearchDisplayGrace)
        {
            var completedResearch = lastCompletedResearchOperation.Event;
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.Open,
                OperationKind = CodexOperationKind.Research,
                IsMcpOperation = completedResearch.IsMcpOperation,
                McpServerName = completedResearch.McpServerName,
                ActiveMcpServerNames = completedResearch.IsMcpOperation &&
                    !string.IsNullOrWhiteSpace(completedResearch.McpServerName)
                    ? [completedResearch.McpServerName]
                    : Array.Empty<string>(),
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                PendingOperationCount = 0,
                PendingMutationCount = 0,
                TriggerEvent = lastEffectiveEvent,
                ActiveOperationEvent = completedResearch,
                Reason = "research operation completed; waiting for next Codex event",
                Source = completedResearch.Source
            };
        }

        var effectiveAge = lastEffectiveSignalAtUtc.HasValue
            ? nowUtc - lastEffectiveSignalAtUtc.Value
            : _staleAfter;
        if (effectiveAge >= _staleAfter)
        {
            return new CodexActivityState
            {
                Lifecycle = CodexTurnLifecycle.Stalled,
                TurnId = currentTurnId,
                LatestThinkingSummary = latestThinkingSummary,
                TurnStartedAtUtc = turnStartedAtUtc,
                LastEventAtUtc = lastEventAtUtc,
                LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
                MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
                TriggerEvent = lastEffectiveEvent,
                Reason = $"no effective event for {Math.Max(0, (int)effectiveAge.TotalSeconds)} seconds",
                Source = lastEffectiveEvent?.Source ?? CodexActivitySource.SessionLog
            };
        }

        return new CodexActivityState
        {
            Lifecycle = CodexTurnLifecycle.Open,
            OperationKind = CodexOperationKind.Unknown,
            TurnId = currentTurnId,
            LatestThinkingSummary = latestThinkingSummary,
            TurnStartedAtUtc = turnStartedAtUtc,
            LastEventAtUtc = lastEventAtUtc,
            LastEffectiveSignalAtUtc = lastEffectiveSignalAtUtc,
            MutationFilePaths = mutationPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(),
            TriggerEvent = lastEffectiveEvent,
            Reason = effectiveAge <= _reasoningGrace
                ? "recent turn or operation event without a pending operation"
                : "turn is open without a classified pending operation",
            Source = lastEffectiveEvent?.Source ?? CodexActivitySource.SessionLog
        };
    }

    private static string? ResolveActiveFilePath(
        PendingOperation activeOperation,
        IReadOnlyList<PendingOperation> pending)
    {
        if (!IsMutation(activeOperation.Event.OperationKind))
        {
            return null;
        }

        if (activeOperation.Event.TargetPaths.Count == 1)
        {
            return activeOperation.Event.TargetPaths[0];
        }

        if (activeOperation.Event.TargetPaths.Count > 1)
        {
            return null;
        }

        return pending
            .Where(operation => IsMutation(operation.Event.OperationKind))
            .SelectMany(operation => operation.Event.TargetPaths)
            .LastOrDefault(path => !string.IsNullOrWhiteSpace(path));
    }

    private static PendingOperation? CompleteOperation(
        CodexActivityEvent activityEvent,
        IDictionary<string, PendingOperation> pendingOperations,
        ICollection<PendingOperation> pendingOperationsWithoutId)
    {
        if (!string.IsNullOrWhiteSpace(activityEvent.CallId))
        {
            if (pendingOperations.TryGetValue(activityEvent.CallId, out var operation))
            {
                pendingOperations.Remove(activityEvent.CallId);
                return operation;
            }

            return null;
        }

        if (pendingOperationsWithoutId.Count == 1)
        {
            var operation = pendingOperationsWithoutId.First();
            pendingOperationsWithoutId.Clear();
            return operation;
        }

        return null;
    }

    private static bool IsCompletedResearchStillCurrent(
        PendingOperation completedResearchOperation,
        long completedResearchOperationSequence,
        CodexActivityEvent? lastEffectiveEvent)
    {
        if (lastEffectiveEvent?.Sequence == completedResearchOperationSequence)
        {
            return true;
        }

        return lastEffectiveEvent?.Kind == CodexActivityEventKind.OperationCompleted &&
            lastEffectiveEvent.OperationKind == CodexOperationKind.Unknown &&
            !string.IsNullOrWhiteSpace(completedResearchOperation.Event.CallId) &&
            string.Equals(
                lastEffectiveEvent.CallId,
                completedResearchOperation.Event.CallId,
                StringComparison.Ordinal);
    }

    private static int CountPendingMutations(
        IReadOnlyDictionary<string, PendingOperation> pendingOperations,
        IEnumerable<PendingOperation> pendingOperationsWithoutId)
    {
        return pendingOperations.Values
            .Concat(pendingOperationsWithoutId)
            .Count(operation => IsMutation(operation.Event.OperationKind));
    }

    private static IReadOnlyList<string> GetActiveMcpServerNames(
        IEnumerable<PendingOperation> pendingOperations)
    {
        return pendingOperations
            .Where(operation => operation.Event.IsMcpOperation)
            .Select(operation => operation.Event.McpServerName)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsMutation(CodexOperationKind operationKind)
    {
        return operationKind is CodexOperationKind.Edit or CodexOperationKind.Create or CodexOperationKind.Delete;
    }

    private static bool BelongsToCurrentTurn(CodexActivityEvent activityEvent, string currentTurnId)
    {
        return string.IsNullOrWhiteSpace(activityEvent.TurnId) ||
            string.Equals(activityEvent.TurnId, currentTurnId, StringComparison.Ordinal);
    }

    private static string? NormalizeTurnId(string? turnId)
    {
        return string.IsNullOrWhiteSpace(turnId) ? null : turnId.Trim();
    }

    private static string? NormalizeInputId(CodexActivityEvent activityEvent)
    {
        return string.IsNullOrWhiteSpace(activityEvent.CallId)
            ? $"input:{activityEvent.Sequence}"
            : activityEvent.CallId.Trim();
    }

    private static DateTime? Max(DateTime? left, DateTime right)
    {
        if (right == default)
        {
            return left;
        }

        return !left.HasValue || right > left.Value ? right : left;
    }

    private sealed record PendingOperation(long Sequence, CodexActivityEvent Event);
}
