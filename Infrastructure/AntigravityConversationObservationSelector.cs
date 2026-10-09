namespace CodexDiscordPresence;

internal static class AntigravityConversationObservationSelector
{
    internal static ProviderObservation? Select(
        IEnumerable<ProviderObservation> observations,
        string? currentConversationId)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var candidates = observations
            .Where(observation => observation.Source == ProviderObservationSource.AntigravityCli)
            .Where(observation => observation.AgentState != ProviderAgentState.Unknown)
            .ToArray();
        if (candidates.Length == 0)
        {
            return null;
        }

        var activeCandidates = candidates
            .Where(observation => IsActive(observation.AgentState))
            .ToArray();
        if (activeCandidates.Length > 0)
        {
            return Rank(activeCandidates);
        }

        if (!string.IsNullOrWhiteSpace(currentConversationId))
        {
            var currentConversation = candidates.FirstOrDefault(observation =>
                string.Equals(
                    observation.ConversationId,
                    currentConversationId,
                    StringComparison.Ordinal));
            if (currentConversation is not null)
            {
                return currentConversation;
            }
        }

        return Rank(candidates);
    }

    internal static ProviderObservation? Select(
        IEnumerable<ProviderObservation> hookObservations,
        IEnumerable<ProviderObservation> statusLineObservations,
        string? currentConversationId)
    {
        ArgumentNullException.ThrowIfNull(hookObservations);
        ArgumentNullException.ThrowIfNull(statusLineObservations);

        return Select(
            MergeHookAndStatusLineObservations(hookObservations, statusLineObservations),
            currentConversationId);
    }

    internal static bool IsActive(ProviderAgentState agentState)
    {
        return agentState is
            ProviderAgentState.Thinking or
            ProviderAgentState.Working or
            ProviderAgentState.ToolUse or
            ProviderAgentState.Initializing;
    }

    private static ProviderObservation Rank(
        IEnumerable<ProviderObservation> observations)
    {
        return observations
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .ThenBy(observation => observation.ConversationId ?? "", StringComparer.Ordinal)
            .First();
    }

    private static IReadOnlyList<ProviderObservation> MergeHookAndStatusLineObservations(
        IEnumerable<ProviderObservation> hookObservations,
        IEnumerable<ProviderObservation> statusLineObservations)
    {
        var hookByConversation = LatestByConversation(hookObservations);
        var statusLineByConversation = LatestByConversation(statusLineObservations);
        var conversationKeys = hookByConversation.Keys
            .Concat(statusLineByConversation.Keys)
            .Distinct(StringComparer.Ordinal);
        var merged = new List<ProviderObservation>();

        foreach (var conversationKey in conversationKeys)
        {
            hookByConversation.TryGetValue(conversationKey, out var hookObservation);
            statusLineByConversation.TryGetValue(conversationKey, out var statusLineObservation);
            merged.Add(Merge(hookObservation, statusLineObservation));
        }

        return merged;
    }

    private static Dictionary<string, ProviderObservation> LatestByConversation(
        IEnumerable<ProviderObservation> observations)
    {
        return observations
            .Where(observation => observation.Source == ProviderObservationSource.AntigravityCli)
            .Where(observation => observation.AgentState != ProviderAgentState.Unknown)
            .GroupBy(GetConversationKey, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.OrderByDescending(observation => observation.ObservedAtUtc).First(),
                StringComparer.Ordinal);
    }

    private static string GetConversationKey(ProviderObservation observation) =>
        string.IsNullOrWhiteSpace(observation.ConversationId)
            ? "<unknown>"
            : observation.ConversationId.Trim();

    private static ProviderObservation Merge(
        ProviderObservation? hookObservation,
        ProviderObservation? statusLineObservation)
    {
        if (hookObservation is null)
        {
            return statusLineObservation!;
        }

        if (statusLineObservation is null)
        {
            return hookObservation;
        }

        return hookObservation with
        {
            Model = hookObservation.Model ?? statusLineObservation.Model,
            Workspace = hookObservation.Workspace ?? statusLineObservation.Workspace,
            ExecutionMode = hookObservation.ExecutionMode == ProviderExecutionMode.Unknown
                ? statusLineObservation.ExecutionMode
                : hookObservation.ExecutionMode,
            ContextWindow = hookObservation.ContextWindow ?? statusLineObservation.ContextWindow,
            ActiveSubagentCount = hookObservation.ActiveSubagentCount ??
                statusLineObservation.ActiveSubagentCount,
            ActiveSubagentWorkKinds = hookObservation.ActiveSubagentCount.HasValue
                ? hookObservation.ActiveSubagentWorkKinds
                : statusLineObservation.ActiveSubagentWorkKinds,
            Quotas = hookObservation.Quotas ?? statusLineObservation.Quotas,
            PlanTier = hookObservation.PlanTier ?? statusLineObservation.PlanTier,
            TranscriptPath = hookObservation.TranscriptPath ?? statusLineObservation.TranscriptPath,
            ArtifactDirectoryPath = hookObservation.ArtifactDirectoryPath ??
                statusLineObservation.ArtifactDirectoryPath,
            Operation = SelectOperation(hookObservation, statusLineObservation),
            IsWaitingForInput = hookObservation.IsWaitingForInput || statusLineObservation.IsWaitingForInput
        };
    }

    private static ProviderOperationObservation? SelectOperation(
        ProviderObservation hookObservation,
        ProviderObservation statusLineObservation)
    {
        if (hookObservation.Operation is null)
        {
            return statusLineObservation.Operation;
        }

        if (statusLineObservation.Operation is null)
        {
            return hookObservation.Operation;
        }

        var hookObservedAt = hookObservation.Operation.ObservedAtUtc ?? hookObservation.ObservedAtUtc;
        var statusLineObservedAt = statusLineObservation.Operation.ObservedAtUtc ?? statusLineObservation.ObservedAtUtc;
        return hookObservedAt >= statusLineObservedAt
            ? hookObservation.Operation
            : statusLineObservation.Operation;
    }
}
