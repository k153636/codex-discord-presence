using DiscordRPC;

namespace CodexDiscordPresence;

internal sealed class DiscordPresenceTransport : IDiscordPresenceTransport
{
    private readonly DiscordRpcClient _client;

    public DiscordPresenceTransport(string clientId)
    {
        _client = new DiscordRpcClient(clientId);
    }

    public bool Initialize() => _client.Initialize();

    public void SetPresence(RichPresence presence) => _client.SetPresence(presence);

    public void ClearPresence() => _client.ClearPresence();

    public void Dispose() => _client.Dispose();
}
