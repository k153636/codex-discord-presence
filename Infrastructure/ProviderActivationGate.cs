namespace CodexDiscordPresence;

internal sealed class ProviderActivationGate
{
    private string? _currentProviderId;
    private bool _hasEvaluated;
    private bool _currentWasActive;
    private DateTimeOffset? _inactiveBoundaryUtc;

    internal ProviderActivationGate(string? initialProviderId)
    {
        _currentProviderId = NormalizeProviderId(initialProviderId);
    }

    internal string? CurrentProviderId => _currentProviderId;

    internal void Reset(string? currentProviderId)
    {
        _currentProviderId = NormalizeProviderId(currentProviderId);
        _hasEvaluated = false;
        _currentWasActive = false;
        _inactiveBoundaryUtc = null;
    }

    internal ProviderSelectionCandidate? Select(
        IEnumerable<ProviderSelectionCandidate> candidates,
        DateTimeOffset nowUtc)
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

        var currentCandidate = FindCurrentCandidate(eligibleCandidates);
        if (!_hasEvaluated)
        {
            _hasEvaluated = true;
            var initialCandidate = SelectActiveCandidate(eligibleCandidates, _currentProviderId);
            if (initialCandidate is null)
            {
                initialCandidate = currentCandidate ??
                    ProviderSelectionPolicy.Select(_currentProviderId, eligibleCandidates);
            }

            SetCurrent(initialCandidate, nowUtc);
            return initialCandidate;
        }

        if (currentCandidate is null)
        {
            if (_currentWasActive)
            {
                _currentWasActive = false;
                _inactiveBoundaryUtc = nowUtc;
            }
            else
            {
                _inactiveBoundaryUtc ??= nowUtc;
            }

            var newlyActiveReplacements = eligibleCandidates
                .Where(candidate => candidate.IsActive)
                .Where(IsAfterInactiveBoundary)
                .ToArray();
            var replacement = SelectActiveCandidate(newlyActiveReplacements, _currentProviderId);
            if (replacement is not null)
            {
                SetCurrent(replacement, nowUtc);
            }

            return replacement;
        }

        if (currentCandidate.IsActive)
        {
            _currentWasActive = true;
            _inactiveBoundaryUtc = null;

            var newerActiveCandidates = eligibleCandidates
                .Where(candidate => candidate.IsActive)
                .Where(candidate => !IsCurrentProvider(candidate))
                .Where(candidate => IsNewerThanCurrent(candidate, currentCandidate))
                .ToArray();
            var replacement = SelectActiveCandidate(newerActiveCandidates, _currentProviderId);
            if (replacement is not null)
            {
                SetCurrent(replacement, nowUtc);
                return replacement;
            }

            return currentCandidate;
        }

        if (_currentWasActive)
        {
            _currentWasActive = false;
            _inactiveBoundaryUtc = currentCandidate.LastObservedAtUtc ?? nowUtc;
        }
        else
        {
            _inactiveBoundaryUtc ??= nowUtc;
        }

        var newlyActiveCandidates = eligibleCandidates
            .Where(candidate => candidate.IsActive)
            .Where(candidate => !IsCurrentProvider(candidate))
            .Where(IsAfterInactiveBoundary)
            .ToArray();
        var nextCandidate = SelectActiveCandidate(newlyActiveCandidates, _currentProviderId);
        if (nextCandidate is not null)
        {
            SetCurrent(nextCandidate, nowUtc);
            return nextCandidate;
        }

        return currentCandidate;
    }

    private ProviderSelectionCandidate? FindCurrentCandidate(
        IEnumerable<ProviderSelectionCandidate> candidates)
    {
        return _currentProviderId is null
            ? null
            : candidates.FirstOrDefault(candidate => IsCurrentProvider(candidate));
    }

    private ProviderSelectionCandidate? SelectActiveCandidate(
        IEnumerable<ProviderSelectionCandidate> candidates,
        string? currentProviderId)
    {
        return ProviderSelectionPolicy.Select(
            currentProviderId,
            candidates.Where(candidate => candidate.IsActive));
    }

    private bool IsAfterInactiveBoundary(ProviderSelectionCandidate candidate)
    {
        return _inactiveBoundaryUtc.HasValue &&
            candidate.LastObservedAtUtc.HasValue &&
            candidate.LastObservedAtUtc.Value > _inactiveBoundaryUtc.Value;
    }

    private static bool IsNewerThanCurrent(
        ProviderSelectionCandidate candidate,
        ProviderSelectionCandidate currentCandidate)
    {
        return candidate.LastObservedAtUtc.HasValue &&
            (!currentCandidate.LastObservedAtUtc.HasValue ||
             candidate.LastObservedAtUtc.Value > currentCandidate.LastObservedAtUtc.Value);
    }

    private bool IsCurrentProvider(ProviderSelectionCandidate candidate)
    {
        return _currentProviderId is not null &&
            string.Equals(
                candidate.ProviderId.Trim(),
                _currentProviderId,
                StringComparison.OrdinalIgnoreCase);
    }

    private void SetCurrent(
        ProviderSelectionCandidate? candidate,
        DateTimeOffset nowUtc)
    {
        if (candidate is null)
        {
            _currentWasActive = false;
            _inactiveBoundaryUtc ??= nowUtc;
            return;
        }

        _currentProviderId = candidate.ProviderId.Trim();
        _currentWasActive = candidate.IsActive;
        _inactiveBoundaryUtc = candidate.IsActive
            ? null
            : candidate.LastObservedAtUtc ?? nowUtc;
    }

    private static bool IsEligible(ProviderSelectionCandidate candidate)
    {
        return candidate.IsEnabled && candidate.IsAvailable;
    }

    private static string? NormalizeProviderId(string? providerId)
    {
        return string.IsNullOrWhiteSpace(providerId) ? null : providerId.Trim();
    }
}
