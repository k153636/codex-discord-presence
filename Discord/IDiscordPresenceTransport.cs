using DiscordRPC;

namespace CodexDiscordPresence;

internal interface IDiscordPresenceTransport : IDisposable
{
    bool Initialize();

    void SetPresence(RichPresence presence);

    void ClearPresence();
}
