namespace CodexDiscordPresence;

public sealed class PresenceRuntimeState
{
    private int _enabled = 1;
    private PresenceDashboardSnapshot _dashboardSnapshot = PresenceDashboardSnapshot.Empty;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Interlocked.Exchange(ref _enabled, value ? 1 : 0);
    }

    public PresenceDashboardSnapshot DashboardSnapshot => Volatile.Read(ref _dashboardSnapshot);

    public void PublishDashboardSnapshot(PresenceDashboardSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Interlocked.Exchange(ref _dashboardSnapshot, snapshot);
    }
}
