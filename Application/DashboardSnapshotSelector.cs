namespace CodexDiscordPresence;

internal sealed class DashboardSnapshotSelector
{
    private PresenceDashboardSnapshot? _current;
    private DateTime _selectedAtUtc;
    private DateTime? _ownerDisplayedAtUtc;
    private DateTime? _presenceDisplayedAtUtc;
    private long? _displayedPublicationGeneration;

    internal string? CurrentProviderId => _current?.ProviderId;

    internal void Reset()
    {
        _current = null;
        _ownerDisplayedAtUtc = null;
        _presenceDisplayedAtUtc = null;
        _displayedPublicationGeneration = null;
    }

    internal PresenceDashboardSnapshot Select(PresenceDashboardSnapshot latest, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(latest);
        if (_current?.ProviderId is not null && !_current.HasNoActiveProvider && _current.Presence is not null &&
            latest.ProviderId is not null && !latest.HasNoActiveProvider &&
            latest.Presence is not null &&
            !string.Equals(latest.ProviderId, _current.ProviderId, StringComparison.OrdinalIgnoreCase) &&
            IsHolding(nowUtc))
        {
            // Connection health stays current while the previous acknowledged card is held.
            return _current with
            {
                IsDiscordConnected = latest.IsDiscordConnected,
                IsDiscordConnecting = latest.IsDiscordConnecting
            };
        }

        if (_current is null ||
            !string.Equals(latest.ProviderId, _current.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            latest.HasNoActiveProvider || latest.Presence is null)
        {
            _selectedAtUtc = nowUtc;
            _ownerDisplayedAtUtc = null;
            _presenceDisplayedAtUtc = null;
            _displayedPublicationGeneration = null;
        }

        _current = latest;
        return latest;
    }

    internal void RecordOwnerDisplayed(PresenceDashboardSnapshot snapshot, DateTime nowUtc)
    {
        if (IsCurrent(snapshot) && snapshot.Presence is not null)
        {
            _ownerDisplayedAtUtc ??= nowUtc;
        }
    }

    internal void RecordPresenceDisplayed(PresenceDashboardSnapshot snapshot, DateTime nowUtc)
    {
        if (IsCurrent(snapshot) && snapshot.PublishedPresence is { } presence &&
            (presence.ProviderId is null ||
             string.Equals(presence.ProviderId, snapshot.ProviderId, StringComparison.OrdinalIgnoreCase)))
        {
            if (_displayedPublicationGeneration != presence.PublicationGeneration)
            {
                _presenceDisplayedAtUtc = nowUtc;
                _displayedPublicationGeneration = presence.PublicationGeneration;
            }
        }
    }

    private bool IsCurrent(PresenceDashboardSnapshot snapshot) =>
        !snapshot.HasNoActiveProvider && snapshot.ProviderId is not null &&
        string.Equals(snapshot.ProviderId, _current?.ProviderId, StringComparison.OrdinalIgnoreCase);

    private bool IsHolding(DateTime nowUtc) =>
        nowUtc - _selectedAtUtc < ProviderActivationGate.MinimumSwitchInterval ||
        _ownerDisplayedAtUtc is { } owner && nowUtc - owner < ProviderActivationGate.MinimumSwitchInterval ||
        _presenceDisplayedAtUtc is { } presence && nowUtc - presence < ProviderActivationGate.MinimumSwitchInterval;
}
