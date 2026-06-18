namespace CodexDiscordPresence;

public sealed record RuntimeTimingSettings(
    int UpdateIntervalSeconds,
    int ActiveUpdateIntervalSeconds,
    int RunningCommandUpdateIntervalSeconds,
    int RunningCommandUpdateIntervalMilliseconds,
    int IdleUpdateIntervalSeconds)
{
    public static RuntimeTimingSettings From(AppOptions options)
    {
        return new RuntimeTimingSettings(
            options.UpdateIntervalSeconds,
            options.Presence.ActiveUpdateIntervalSeconds,
            options.Presence.RunningCommandUpdateIntervalSeconds,
            options.Presence.RunningCommandUpdateIntervalMilliseconds,
            options.Presence.IdleUpdateIntervalSeconds);
    }

    public void ApplyTo(AppOptions options)
    {
        options.UpdateIntervalSeconds = UpdateIntervalSeconds;
        options.Presence.ActiveUpdateIntervalSeconds = ActiveUpdateIntervalSeconds;
        options.Presence.RunningCommandUpdateIntervalSeconds = RunningCommandUpdateIntervalSeconds;
        options.Presence.RunningCommandUpdateIntervalMilliseconds = RunningCommandUpdateIntervalMilliseconds;
        options.Presence.IdleUpdateIntervalSeconds = IdleUpdateIntervalSeconds;
    }
}
