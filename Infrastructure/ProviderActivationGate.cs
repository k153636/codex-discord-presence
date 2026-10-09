namespace CodexDiscordPresence;

internal sealed class ProviderActivationGate
{
    internal static readonly TimeSpan MinimumSwitchInterval = TimeSpan.FromSeconds(5);
    private string? _currentProviderId;
    private DateTimeOffset? _lastSelectionUtc;
    private DateTimeOffset? _dashboardPublishedAtUtc;
    private DateTimeOffset? _confirmedAtUtc;
    private long? _confirmedPublicationGeneration;
    private bool _hasEvaluated;
    private bool _currentWasActive;
    private DateTimeOffset? _inactiveBoundaryUtc;

    internal ProviderActivationGate(string? initialProviderId)
    {
        _currentProviderId = NormalizeProviderId(initialProviderId);
    }

    internal string? CurrentProviderId => _currentProviderId;

    internal bool RecordDashboardPublication(PresenceDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (_lastSelectionUtc is null || snapshot.HasNoActiveProvider || snapshot.Presence is null ||
            !string.Equals(snapshot.ProviderId, _currentProviderId, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var publishedAtUtc = new DateTimeOffset(snapshot.UpdatedAtUtc.ToUniversalTime());
        if (publishedAtUtc < _lastSelectionUtc.Value)
        {
            return false;
        }

        _dashboardPublishedAtUtc ??= publishedAtUtc;
        return RecordPresenceAcknowledgment(snapshot.PublishedPresence, publishedAtUtc);
    }

    internal bool RecordPresenceAcknowledgment(DiscordPresenceSnapshot? presence, DateTimeOffset nowUtc)
    {
        if (presence?.AcknowledgedAtUtc is not { } acknowledgedAtUtc ||
            _lastSelectionUtc is null ||
            !string.Equals(presence.ProviderId, _currentProviderId, StringComparison.OrdinalIgnoreCase) ||
            acknowledgedAtUtc < _lastSelectionUtc.Value.UtcDateTime ||
            acknowledgedAtUtc > nowUtc.UtcDateTime ||
            _confirmedPublicationGeneration == presence.PublicationGeneration)
        {
            return false;
        }

        // Start the hold when the runtime observes the accepted response.
        // Later activity acknowledgments on this connection must not restart it.
        _confirmedAtUtc = nowUtc;
        _confirmedPublicationGeneration = presence.PublicationGeneration;
        return true;
    }

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

            if (initialCandidate is not null && !IsCurrentProvider(initialCandidate))
            {
                if (_lastSelectionUtc is not null)
                {
                    SetCurrent(currentCandidate, nowUtc);
                }
                return SelectReplacement(initialCandidate, currentCandidate, nowUtc);
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
                return SelectReplacement(replacement, currentCandidate, nowUtc);
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
                return SelectReplacement(replacement, currentCandidate, nowUtc);
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
            return SelectReplacement(nextCandidate, currentCandidate, nowUtc);
        }

        return currentCandidate;
    }

    private ProviderSelectionCandidate? SelectReplacement(
        ProviderSelectionCandidate replacement,
        ProviderSelectionCandidate? currentCandidate,
        DateTimeOffset nowUtc)
    {
        // Re-evaluate live candidates on every poll; never queue an obsolete switch.
        if (_lastSelectionUtc is { } lastSelection &&
            (nowUtc - lastSelection < MinimumSwitchInterval ||
             _dashboardPublishedAtUtc is { } displayed && nowUtc - displayed < MinimumSwitchInterval ||
             _confirmedAtUtc is { } confirmed && nowUtc - confirmed < MinimumSwitchInterval ||
             currentCandidate is not null && _confirmedAtUtc is null &&
                 nowUtc - lastSelection < DiscordPresenceClient.ResponseTimeout))
        {
            return currentCandidate;
        }
        SetCurrent(replacement, nowUtc);
        return replacement;
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
            GetActivityTimestamp(candidate) is { } activityTimestamp &&
            activityTimestamp > _inactiveBoundaryUtc.Value;
    }

    private static bool IsNewerThanCurrent(
        ProviderSelectionCandidate candidate,
        ProviderSelectionCandidate currentCandidate)
    {
        var candidateTimestamp = GetActivityTimestamp(candidate);
        var currentTimestamp = GetActivityTimestamp(currentCandidate);
        return candidateTimestamp.HasValue &&
            (!currentTimestamp.HasValue || candidateTimestamp.Value > currentTimestamp.Value);
    }

    private static DateTimeOffset? GetActivityTimestamp(ProviderSelectionCandidate candidate)
    {
        return ProviderSelectionPolicy.GetActivityTimestamp(candidate);
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

        if (_lastSelectionUtc is null || !IsCurrentProvider(candidate))
        {
            _lastSelectionUtc = nowUtc;
            _dashboardPublishedAtUtc = null;
            _confirmedAtUtc = null;
            _confirmedPublicationGeneration = null;
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
