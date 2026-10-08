namespace CodexDiscordPresence;

internal static class UpdateRestartPolicy
{
    public static bool CanRestart(PresenceRuntimeState state, bool dashboardOpen, DateTime nowUtc)
    {
        if (dashboardOpen) return false;
        if (!state.Enabled) return true;
        var snapshot = state.DashboardSnapshot;
        return snapshot.Presence is not null && snapshot.UpdatedAtUtc <= nowUtc &&
            nowUtc - snapshot.UpdatedAtUtc <= TimeSpan.FromSeconds(15) &&
            snapshot.Presence.ActivityKind is CodexActivityKind.Offline or CodexActivityKind.Ready or CodexActivityKind.WaitingForInput;
    }
}
