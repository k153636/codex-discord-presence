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
        return new SessionSnapshot(_startedAt, DateTime.UtcNow - _startedAt);
    }
}
