namespace CodexDiscordPresence;

public sealed class PresenceRuntimeState
{
    private int _enabled = 1;
    private PresenceDashboardSnapshot _dashboardSnapshot = PresenceDashboardSnapshot.Empty;
    private readonly object _providerSync = new();
    private Dictionary<string, bool> _providerEnabled = new(StringComparer.OrdinalIgnoreCase);

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Interlocked.Exchange(ref _enabled, value ? 1 : 0);
    }

    public PresenceDashboardSnapshot DashboardSnapshot => Volatile.Read(ref _dashboardSnapshot);

    public IReadOnlyDictionary<string, bool> ProviderEnabled
    {
        get
        {
            lock (_providerSync)
            {
                return new Dictionary<string, bool>(_providerEnabled, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    public bool IsProviderEnabled(string providerId, bool defaultValue = true)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            return defaultValue;
        }

        lock (_providerSync)
        {
            return _providerEnabled.TryGetValue(providerId.Trim(), out var enabled)
                ? enabled
                : defaultValue;
        }
    }

    public void InitializeProviderEnabled(IReadOnlyDictionary<string, bool>? providerEnabled)
    {
        var snapshot = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (providerEnabled is not null)
        {
            foreach (var (providerId, enabled) in providerEnabled)
            {
                if (!string.IsNullOrWhiteSpace(providerId))
                {
                    snapshot[providerId.Trim()] = enabled;
                }
            }
        }

        lock (_providerSync)
        {
            _providerEnabled = snapshot;
        }
    }

    public void SetProviderEnabled(string providerId, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("A provider ID is required.", nameof(providerId));
        }

        lock (_providerSync)
        {
            _providerEnabled[providerId.Trim()] = enabled;
        }
    }

    public void PublishDashboardSnapshot(PresenceDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _dashboardSnapshot, snapshot);
    }
}
