namespace CodexDiscordPresence;

internal sealed record ProviderSelectionCandidate(
    string ProviderId,
    bool IsEnabled,
    bool IsAvailable,
    DateTimeOffset? LastObservedAtUtc,
    bool HasProjectPath,
    bool IsProjectMatch,
    int DetectionStrength = 0,
    bool IsActive = false,
    DateTimeOffset? ActivityStartedAtUtc = null,
    DateTimeOffset? LastActivityEventAtUtc = null);

internal static class ProviderSelectionPolicy
{
    public static ProviderSelectionCandidate? Select(
        string? currentProviderId,
        IEnumerable<ProviderSelectionCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var eligibleCandidates = candidates
            .Where(IsEligible)
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate.ProviderId))
            .ToArray();
        if (eligibleCandidates.Length == 0)
        {
            return null;
        }

        var projectMatches = eligibleCandidates
            .Where(candidate => candidate.HasProjectPath && candidate.IsProjectMatch)
            .ToArray();
        var candidatesToRank = projectMatches.Length > 0
            ? projectMatches
            : eligibleCandidates;
        var normalizedCurrentProviderId = currentProviderId?.Trim();

        return candidatesToRank
            .OrderByDescending(GetSelectionTimestamp)
            .ThenByDescending(candidate => candidate.DetectionStrength)
            .ThenByDescending(candidate => IsCurrentProvider(candidate, normalizedCurrentProviderId))
            .ThenBy(candidate => candidate.ProviderId, StringComparer.Ordinal)
            .First();
    }

    private static DateTimeOffset GetSelectionTimestamp(ProviderSelectionCandidate candidate)
    {
        return (candidate.IsActive
                ? GetActivityTimestamp(candidate)
                : null) ??
            candidate.LastObservedAtUtc ??
            DateTimeOffset.MinValue;
    }

    internal static DateTimeOffset? GetActivityTimestamp(ProviderSelectionCandidate candidate)
    {
        // A refresh observation is not necessarily a new provider activity.
        return candidate.LastActivityEventAtUtc ?? candidate.ActivityStartedAtUtc ?? candidate.LastObservedAtUtc;
    }

    private static bool IsEligible(ProviderSelectionCandidate candidate)
    {
        return candidate.IsEnabled && candidate.IsAvailable;
    }

    private static bool IsCurrentProvider(
        ProviderSelectionCandidate candidate,
        string? currentProviderId)
    {
        return currentProviderId is not null &&
            string.Equals(candidate.ProviderId.Trim(), currentProviderId, StringComparison.OrdinalIgnoreCase);
    }
}
