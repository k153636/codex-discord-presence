namespace CodexDiscordPresence;

public sealed class SessionClock
{
    private readonly DateTime _startedAt = DateTime.UtcNow;

    public SessionSnapshot GetSnapshot()
    {
        return new SessionSnapshot(_startedAt, DateTime.UtcNow - _startedAt);
    }
}
