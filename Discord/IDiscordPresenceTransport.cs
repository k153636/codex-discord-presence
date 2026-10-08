using DiscordRPC;

namespace CodexDiscordPresence;

internal interface IDiscordPresenceTransport : IDisposable
{
    bool Initialize();

    bool TryDequeueNotification(out DiscordPresenceNotification? notification);

    void SetPresence(RichPresence presence);

    void ClearPresence();
}
