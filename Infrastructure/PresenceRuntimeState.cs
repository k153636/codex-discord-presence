namespace CodexDiscordPresence;

public sealed class PresenceRuntimeState
{
    private int _enabled = 1;

    public bool Enabled
    {
        get => Volatile.Read(ref _enabled) == 1;
        set => Interlocked.Exchange(ref _enabled, value ? 1 : 0);
    }
}
