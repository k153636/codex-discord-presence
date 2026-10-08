using DiscordRPC;
using DiscordRPC.Message;
using System.Collections.Concurrent;

namespace CodexDiscordPresence;

internal sealed class DiscordPresenceTransport : IDiscordPresenceTransport
{
    private readonly DiscordRpcClient _client;
    private readonly ConcurrentQueue<DiscordPresenceNotification> _notifications = new();

    public DiscordPresenceTransport(string clientId)
        : this(new DiscordRpcClient(clientId))
    {
    }

    internal DiscordPresenceTransport(DiscordRpcClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        // Every requested update must produce an acknowledgment, including keepalives.
        _client = client;
        _client.SkipIdenticalPresence = false;
        _client.OnConnectionEstablished += (_, _) => Enqueue(DiscordPresenceNotificationKind.Connecting);
        _client.OnConnectionFailed += (_, _) => Enqueue(DiscordPresenceNotificationKind.Closed);
        _client.OnReady += (_, _) => Enqueue(DiscordPresenceNotificationKind.Ready);
        _client.OnClose += (_, _) => Enqueue(DiscordPresenceNotificationKind.Closed);
        _client.OnError += (_, message) => _notifications.Enqueue(new(
            DiscordPresenceNotificationKind.Error, ErrorCode: message.Code.ToString()));
        _client.OnRpcMessage += CaptureAcknowledgment;
    }

    public bool Initialize() => _client.Initialize();

    public bool TryDequeueNotification(out DiscordPresenceNotification? notification) =>
        _notifications.TryDequeue(out notification);

    public void SetPresence(RichPresence presence) => _client.SetPresence(presence);

    public void ClearPresence() => _client.ClearPresence();

    public void Dispose() => _client.Dispose();

    private void Enqueue(DiscordPresenceNotificationKind kind) => _notifications.Enqueue(new(kind));

    private void CaptureAcknowledgment(object sender, IMessage message)
    {
        if (message is not PresenceMessage acknowledgment)
        {
            return;
        }

        // OnPresenceUpdate runs after the library merges this response with its latest
        // local request. Capture the response here so a delayed ack cannot confirm that request.
        var presence = acknowledgment.Presence;
        var snapshot = presence is null
            ? null
            : new DiscordPresenceSnapshot(
                presence.Details,
                presence.State,
                presence.Assets?.LargeImageKey,
                presence.Assets?.LargeImageText,
                presence.Assets?.SmallImageKey,
                presence.Assets?.SmallImageText,
                presence.Timestamps?.Start,
                presence.Party?.Size,
                presence.Party?.Max,
                Array.Empty<RenderedButton>())
            {
                ActivityType = presence.Type
            };
        _notifications.Enqueue(new(DiscordPresenceNotificationKind.PresenceAcknowledged, snapshot));
    }
}
