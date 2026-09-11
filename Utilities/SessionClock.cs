namespace CodexDiscordPresence;

public sealed class SessionClock
{
    private readonly DateTime _startedAt;

    public SessionClock(DateTime startedAtUtc)
    {
        _startedAt = startedAtUtc.Kind == DateTimeKind.Utc
            ? startedAtUtc
            : startedAtUtc.ToUniversalTime();
    }

    public SessionSnapshot GetSnapshot()
    {
        return GetSnapshot(DateTime.UtcNow);
    }

    internal SessionSnapshot GetSnapshot(DateTime nowUtc)
    {
        var normalizedNowUtc = nowUtc.Kind == DateTimeKind.Utc
            ? nowUtc
            : nowUtc.ToUniversalTime();
        return new SessionSnapshot(_startedAt, normalizedNowUtc - _startedAt);
    }
}
