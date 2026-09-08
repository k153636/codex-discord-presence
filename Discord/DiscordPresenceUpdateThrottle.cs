namespace CodexDiscordPresence;

internal sealed class DiscordPresenceUpdateThrottle
{
    internal const int MaxUpdatesPerWindow = 5;
    internal static readonly TimeSpan Window = TimeSpan.FromSeconds(20);

    private readonly Queue<DateTime> _reservedAtUtc = new();

    public bool TryReserve(DateTime nowUtc, out DateTime retryAtUtc)
    {
        Prune(nowUtc);
        if (_reservedAtUtc.Count >= MaxUpdatesPerWindow)
        {
            retryAtUtc = _reservedAtUtc.Peek().Add(Window);
            return false;
        }

        _reservedAtUtc.Enqueue(nowUtc);
        retryAtUtc = nowUtc;
        return true;
    }

    public void Reset()
    {
        _reservedAtUtc.Clear();
    }

    private void Prune(DateTime nowUtc)
    {
        while (_reservedAtUtc.Count > 0 && nowUtc - _reservedAtUtc.Peek() >= Window)
        {
            _reservedAtUtc.Dequeue();
        }
    }
}
