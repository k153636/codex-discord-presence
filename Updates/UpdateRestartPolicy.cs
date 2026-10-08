namespace CodexDiscordPresence;

internal static class UpdateRestartPolicy
{
    public static bool CanRestart(PresenceRuntimeState state, bool dashboardOpen, DateTime nowUtc)
    {
        if (dashboardOpen) return false;
        if (!state.Enabled) return true;
        var snapshot = state.DashboardSnapshot;
        if (snapshot.UpdatedAtUtc > nowUtc || nowUtc - snapshot.UpdatedAtUtc > TimeSpan.FromSeconds(15))
            return false;
        if (snapshot.Presence is null) return snapshot.HasNoActiveProvider;
        return snapshot.Presence.ActivityKind is CodexActivityKind.Offline or CodexActivityKind.Ready or CodexActivityKind.WaitingForInput;
    }
}
