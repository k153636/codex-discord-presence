using System.Reflection;
using DiscordRPC;
using DiscordRPC.Message;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordPresenceTransportTests
{
    [Fact]
    public void Notification_CapturesRawAcknowledgmentBeforeSdkMergesLatestRequest()
    {
        using var sdk = new DiscordRpcClient("test-client-id");
        using var transport = new DiscordPresenceTransport(sdk);
        sdk.SetPresence(new RichPresence
        {
            Details = "new request",
            State = "new request state",
            Assets = new Assets { LargeImageKey = "rpc_new" },
            Buttons = [new Button { Label = "New request", Url = "https://example.com/new" }]
        });
        var message = CreateAcknowledgment(new RichPresence { Details = "old response" });

        DeliverSdkMessage(sdk, message);

        Assert.True(transport.TryDequeueNotification(out var notification));
        Assert.Equal(DiscordPresenceNotificationKind.PresenceAcknowledged, notification?.Kind);
        Assert.NotNull(notification?.Presence);
        Assert.Equal("old response", notification.Presence.Details);
        Assert.Null(notification.Presence.State);
        Assert.Null(notification.Presence.LargeImageKey);
        Assert.Empty(notification.Presence.Buttons);
        // Exercise the installed SDK's actual merge, rather than simulate it in a fake.
        var merged = Assert.IsType<RichPresence>(message.Presence);
        Assert.Equal("https://example.com/new", Assert.Single(merged.Buttons).Url);
        merged.Details = "mutated after capture";
        Assert.Equal("old response", notification.Presence.Details);
        Assert.False(transport.TryDequeueNotification(out _));
        Assert.False(sdk.SkipIdenticalPresence);
    }

    [Fact]
    public void Notification_NullSdkAcknowledgmentRepresentsConfirmedClear()
    {
        using var sdk = new DiscordRpcClient("test-client-id");
        using var transport = new DiscordPresenceTransport(sdk);
        sdk.SetPresence(new RichPresence { Details = "latest local request" });

        DeliverSdkMessage(sdk, CreateAcknowledgment(null));

        Assert.True(transport.TryDequeueNotification(out var notification));
        Assert.Equal(DiscordPresenceNotificationKind.PresenceAcknowledged, notification?.Kind);
        Assert.Null(notification?.Presence);
        Assert.Null(sdk.CurrentPresence);
    }

    private static PresenceMessage CreateAcknowledgment(BaseRichPresence? presence)
    {
        // The SDK's received message constructors/setters are internal. Reflection is
        // confined to tests so the real SDK event pipeline can run without opening IPC.
        var message = Assert.IsType<PresenceMessage>(Activator.CreateInstance(typeof(PresenceMessage), nonPublic: true));
        var property = typeof(PresenceMessage).GetProperty(nameof(PresenceMessage.Presence));
        Assert.NotNull(property);
        property.SetValue(message, presence);
        return message;
    }

    private static void DeliverSdkMessage(DiscordRpcClient sdk, IMessage message)
    {
        var field = typeof(DiscordRpcClient).GetField("connection", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        var connection = field.GetValue(sdk);
        Assert.NotNull(connection);
        var enqueue = connection.GetType().GetMethod("EnqueueMessage", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(enqueue);
        enqueue.Invoke(connection, [message]);
    }
}
