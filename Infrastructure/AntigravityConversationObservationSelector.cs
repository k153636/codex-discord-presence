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

    internal static bool IsActive(ProviderAgentState agentState)
    {
        return agentState is
            ProviderAgentState.Thinking or
            ProviderAgentState.Working or
            ProviderAgentState.ToolUse;
    }

    private static ProviderObservation Rank(
        IEnumerable<ProviderObservation> observations)
    {
        return observations
            .OrderByDescending(observation => observation.ObservedAtUtc)
            .ThenBy(observation => observation.ConversationId ?? "", StringComparer.Ordinal)
            .First();
    }
}
