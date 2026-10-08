using DiscordRPC;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordPresenceClientTests
{
    [Fact]
    public async Task StartAndUpdate_RetriesAfterTransportInitializationFailure()
    {
        var firstTransport = new FakeDiscordPresenceTransport { InitializeResult = false };
        var secondTransport = new FakeDiscordPresenceTransport();
        var transports = new Queue<FakeDiscordPresenceTransport>([firstTransport, secondTransport]);
        var now = DateTime.UtcNow;
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transports.Dequeue(),
                () => now);

            await client.StartAsync(CancellationToken.None);
            Assert.False(client.IsConnected);

            now = now.Add(DiscordReconnectBackoff.GetDelay(1) + TimeSpan.FromMilliseconds(1));

            Assert.True(client.Update(CreatePresence()));
            Assert.True(client.IsConnected);
            Assert.Single(secondTransport.SetPresenceCalls);
            Assert.False(client.NeedsPresenceRefresh);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Update_RetriesAfterSetFailureAndPublishesOnReconnectedTransport()
    {
        var firstTransport = new FakeDiscordPresenceTransport { ThrowOnSetPresence = true };
        var secondTransport = new FakeDiscordPresenceTransport();
        var transports = new Queue<FakeDiscordPresenceTransport>([firstTransport, secondTransport]);
        var now = DateTime.UtcNow;
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transports.Dequeue(),
                () => now);

            await client.StartAsync(CancellationToken.None);

            Assert.False(client.Update(CreatePresence()));
            Assert.False(client.IsConnected);
            Assert.Null(client.LastPublishedPresence);

            now = now.Add(DiscordReconnectBackoff.GetDelay(1) + TimeSpan.FromMilliseconds(1));

            Assert.True(client.Update(CreatePresence()));
            Assert.True(client.IsConnected);
            Assert.Single(secondTransport.SetPresenceCalls);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Clear_DefersWhenRateLimitedAndRetriesAfterTheWindow()
    {
        var transport = new FakeDiscordPresenceTransport();
        var now = DateTime.UtcNow;
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transport,
                () => now);

            await client.StartAsync(CancellationToken.None);
            for (var index = 0; index < DiscordPresenceUpdateThrottle.MaxUpdatesPerWindow; index++)
            {
                Assert.True(client.Update(CreatePresence()));
            }

            client.Clear();

            Assert.Empty(transport.ClearPresenceCalls);
            Assert.NotNull(client.LastPublishedPresence);

            now = now.Add(DiscordPresenceUpdateThrottle.Window);
            client.Clear();

            Assert.Single(transport.ClearPresenceCalls);
            Assert.Null(client.LastPublishedPresence);
            client.Clear();
            Assert.Single(transport.ClearPresenceCalls);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Clear_ClearsEvenWhenNoPresenceWasPublishedOnThisConnection()
    {
        var transport = new FakeDiscordPresenceTransport();
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transport,
                () => DateTime.UtcNow);

            await client.StartAsync(CancellationToken.None);
            client.Clear();

            Assert.Single(transport.ClearPresenceCalls);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task ClearFailure_RemainsPendingAcrossReconnectAndRetriesBeforeNextUpdate()
    {
        var firstTransport = new FakeDiscordPresenceTransport { ThrowOnClearPresence = true };
        var secondTransport = new FakeDiscordPresenceTransport();
        var transports = new Queue<FakeDiscordPresenceTransport>([firstTransport, secondTransport]);
        var now = DateTime.UtcNow;
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transports.Dequeue(),
                () => now);

            await client.StartAsync(CancellationToken.None);
            client.Clear();
            Assert.Empty(secondTransport.ClearPresenceCalls);

            now = now.Add(DiscordReconnectBackoff.GetDelay(1) + TimeSpan.FromMilliseconds(1));

            Assert.True(client.Update(CreatePresence()));
            Assert.Single(secondTransport.ClearPresenceCalls);
            Assert.Single(secondTransport.SetPresenceCalls);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task RequestPresenceRefresh_ForcesTheNextUpdateAfterThePresenceWasPublished()
    {
        var transport = new FakeDiscordPresenceTransport();
        var now = DateTime.UtcNow;
        var tempPath = CreateTempDirectory();

        try
        {
            using var log = new DiagnosticLog(Path.Combine(tempPath, "rpc.log"));
            using var client = new DiscordPresenceClient(
                CreateOptions(),
                log,
                _ => transport,
                () => now);

            await client.StartAsync(CancellationToken.None);
            Assert.True(client.Update(CreatePresence()));
            Assert.False(client.NeedsPresenceRefresh);

            client.RequestPresenceRefresh();

            Assert.True(client.NeedsPresenceRefresh);
            Assert.True(client.Update(CreatePresence()));
            Assert.Equal(2, transport.SetPresenceCalls.Count);
            Assert.False(client.NeedsPresenceRefresh);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public async Task Start_InitializationPendingWaitsForReadyWithoutReplacingTransport()
    {
        var transport = new FakeDiscordPresenceTransport { AutoReady = false };
        using var fixture = new ClientFixture(transport);
        Assert.False(fixture.Client.IsConnecting);
        await fixture.Client.StartAsync(CancellationToken.None);

        Assert.False(fixture.Client.IsConnected);
        Assert.True(fixture.Client.IsConnecting);
        Assert.False(fixture.Client.Update(CreatePresence()));
        Assert.Equal(1, transport.InitializeCalls);
        Assert.False(transport.Disposed);
        Assert.Empty(transport.SetPresenceCalls);
        Assert.DoesNotContain("Discord RPC initialized.", File.ReadAllText(fixture.Log.Path));

        transport.Emit(new(DiscordPresenceNotificationKind.Ready));
        fixture.Client.ProcessPendingNotifications();

        Assert.True(fixture.Client.IsConnected);
        Assert.False(fixture.Client.IsConnecting);
        Assert.True(fixture.Client.NeedsPresenceRefresh);
        Assert.True(fixture.Client.Update(CreatePresence()));
    }

    [Fact]
    public async Task Update_OnlyMatchingAcknowledgmentPublishesAndAllowsNextRequest()
    {
        var transport = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        Assert.True(fixture.Client.Update(CreatePresence()));
        Assert.Null(fixture.Client.LastPublishedPresence);
        Assert.False(fixture.Client.Update(CreatePresence() with { State = "new state" }));
        Assert.Single(transport.SetPresenceCalls);

        transport.Acknowledge(new RichPresence { Details = "unexpected", State = "state" });
        fixture.Client.ProcessPendingNotifications();
        Assert.Null(fixture.Client.LastPublishedPresence);
        Assert.False(fixture.Client.Update(CreatePresence() with { State = "new state" }));

        transport.Acknowledge(transport.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal("state", fixture.Client.LastPublishedPresence?.State);
        Assert.True(fixture.Client.Update(CreatePresence() with { State = "new state" }));

        transport.Acknowledge(transport.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal("state", fixture.Client.LastPublishedPresence?.State);
        transport.Acknowledge(transport.SetPresenceCalls[1]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal("new state", fixture.Client.LastPublishedPresence?.State);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Notifications_CloseOrAsyncErrorReconnectsAndIgnoresOldTransport(
        bool error)
    {
        var kind = error ? DiscordPresenceNotificationKind.Error : DiscordPresenceNotificationKind.Closed;
        var first = new FakeDiscordPresenceTransport();
        var second = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(first, second);
        await fixture.Client.StartAsync(CancellationToken.None);
        Assert.True(fixture.Client.Update(CreatePresence()));

        await Task.Run(() => first.Emit(new(kind, ErrorCode: "InvalidPayload")));
        // Callbacks must not mutate the client from the RPC thread.
        Assert.True(fixture.Client.IsConnected);
        fixture.Client.ProcessPendingNotifications();
        Assert.False(fixture.Client.IsConnected);
        Assert.False(fixture.Client.IsConnecting);
        Assert.Null(fixture.Client.LastPublishedPresence);
        Assert.True(fixture.Client.NeedsPresenceRefresh);
        Assert.True(first.Disposed);
        Assert.False(fixture.Client.Update(CreatePresence()));

        fixture.Now = fixture.Now.Add(DiscordReconnectBackoff.GetDelay(1));
        Assert.True(fixture.Client.Update(CreatePresence() with { State = "reconnected" }));
        first.Acknowledge(first.SetPresenceCalls[0]);
        first.Emit(new(DiscordPresenceNotificationKind.Ready));
        first.Emit(new(DiscordPresenceNotificationKind.Error));
        fixture.Client.ProcessPendingNotifications();
        Assert.True(fixture.Client.IsConnected);
        Assert.Null(fixture.Client.LastPublishedPresence);

        second.Acknowledge(second.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal("reconnected", fixture.Client.LastPublishedPresence?.State);
    }

    [Fact]
    public async Task UpdateOptions_ProviderSwitchDiscardsPreviousAcknowledgment()
    {
        var first = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        var second = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(first, second);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Update(CreatePresence());
        fixture.Client.UpdateOptions(new DiscordOptions { ClientId = "another-provider" });
        Assert.False(fixture.Client.IsConnected);
        Assert.True(first.Disposed);
        Assert.True(fixture.Client.Update(CreatePresence() with { Details = "another provider" }));

        first.Acknowledge(first.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Null(fixture.Client.LastPublishedPresence);
        second.Acknowledge(second.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal("another provider", fixture.Client.LastPublishedPresence?.Details);
    }

    [Fact]
    public async Task Clear_WaitsForSetAcknowledgmentAndClearAcknowledgment()
    {
        var transport = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Update(CreatePresence());
        fixture.Client.Clear();
        Assert.Empty(transport.ClearPresenceCalls);

        transport.Acknowledge(transport.SetPresenceCalls[0]);
        fixture.Client.Clear();
        Assert.Single(transport.ClearPresenceCalls);
        Assert.NotNull(fixture.Client.LastPublishedPresence);
        Assert.False(fixture.Client.Update(CreatePresence() with { State = "next" }));
        transport.Acknowledge(null);
        fixture.Client.ProcessPendingNotifications();
        Assert.Null(fixture.Client.LastPublishedPresence);

        transport.Acknowledge(transport.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Null(fixture.Client.LastPublishedPresence);
        Assert.True(fixture.Client.Update(CreatePresence() with { State = "next" }));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Notifications_ResponseTimeoutReconnectsForPendingReadyOrAcknowledgment(bool ready)
    {
        var first = new FakeDiscordPresenceTransport { AutoReady = ready, AutoAcknowledge = false };
        var second = new FakeDiscordPresenceTransport();
        using var fixture = new ClientFixture(first, second);
        await fixture.Client.StartAsync(CancellationToken.None);
        if (ready)
        {
            Assert.True(fixture.Client.Update(CreatePresence()));
        }
        fixture.Now = fixture.Now.Add(DiscordPresenceClient.ResponseTimeout);
        fixture.Client.ProcessPendingNotifications();
        Assert.False(fixture.Client.IsConnected);
        Assert.True(first.Disposed);
        Assert.True(fixture.Client.NeedsPresenceRefresh);
        fixture.Now = fixture.Now.Add(DiscordReconnectBackoff.GetDelay(1));
        Assert.True(fixture.Client.Update(CreatePresence()));
    }

    [Fact]
    public async Task Notifications_ReestablishedTransportRequiresReadyAndPresenceRefresh()
    {
        var transport = new FakeDiscordPresenceTransport();
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Update(CreatePresence());
        transport.Emit(new(DiscordPresenceNotificationKind.Connecting));
        fixture.Client.ProcessPendingNotifications();
        Assert.False(fixture.Client.IsConnected);
        Assert.True(fixture.Client.IsConnecting);
        Assert.Null(fixture.Client.LastPublishedPresence);
        Assert.False(fixture.Client.Update(CreatePresence()));
        transport.Emit(new(DiscordPresenceNotificationKind.Ready));
        fixture.Client.ProcessPendingNotifications();
        Assert.True(fixture.Client.IsConnected);
        Assert.False(fixture.Client.IsConnecting);
        Assert.True(fixture.Client.NeedsPresenceRefresh);
        Assert.True(fixture.Client.Update(CreatePresence()));
    }

    [Fact]
    public async Task Dispose_DelayedReadyCannotReconnectDisposedClient()
    {
        var transport = new FakeDiscordPresenceTransport { AutoReady = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Dispose();
        transport.Emit(new(DiscordPresenceNotificationKind.Ready));
        fixture.Client.ProcessPendingNotifications();
        Assert.False(fixture.Client.IsConnected);
        Assert.False(fixture.Client.IsConnecting);
        Assert.False(fixture.Client.Update(CreatePresence()));
    }

    [Fact]
    public async Task Acknowledgment_ResolvesNumericAndExternalAssetsToRequestedPreviewReferences()
    {
        var transport = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.UpdateOptions(new DiscordOptions
        {
            ClientId = "test-client-id",
            SmallImageKey = "rpc_codex",
            ExternalImageUrls = new Dictionary<string, string>
            {
                ["rpc_codex"] = "https://example.com/codex.gif"
            }
        });
        Assert.True(fixture.Client.Update(CreatePresence()));
        var requested = DiscordPresenceSnapshot.From(transport.SetPresenceCalls[0]);
        var acknowledged = requested with
        {
            LargeImageKey = "1234567890",
            SmallImageKey = "mp:external/abc",
            Buttons = Array.Empty<RenderedButton>()
        };
        transport.Emit(new(DiscordPresenceNotificationKind.PresenceAcknowledged, acknowledged));
        fixture.Client.ProcessPendingNotifications();

        Assert.Equal(requested.LargeImageKey, fixture.Client.LastPublishedPresence?.LargeImageKey);
        Assert.Equal(requested.SmallImageKey, fixture.Client.LastPublishedPresence?.SmallImageKey);
        Assert.Empty(fixture.Client.LastPublishedPresence!.Buttons);
    }

    [Fact]
    public async Task Acknowledgment_DoesNotInventAssetsMissingFromResponse()
    {
        var transport = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Update(CreatePresence());
        var requested = DiscordPresenceSnapshot.From(transport.SetPresenceCalls[0]);
        transport.Emit(new(DiscordPresenceNotificationKind.PresenceAcknowledged, requested with
        {
            LargeImageKey = null,
            SmallImageKey = null
        }));
        fixture.Client.ProcessPendingNotifications();

        Assert.NotNull(fixture.Client.LastPublishedPresence);
        Assert.Null(fixture.Client.LastPublishedPresence.LargeImageKey);
        Assert.Null(fixture.Client.LastPublishedPresence.SmallImageKey);
    }

    [Fact]
    public async Task Acknowledgment_DoesNotPublishMismatchedPartyOrSessionTimestamp()
    {
        var transport = new FakeDiscordPresenceTransport { AutoAcknowledge = false };
        using var fixture = new ClientFixture(transport);
        await fixture.Client.StartAsync(CancellationToken.None);
        fixture.Client.Update(CreatePresence() with { StartedAt = fixture.Now, PartySize = 3 });
        var requested = DiscordPresenceSnapshot.From(transport.SetPresenceCalls[0]);
        transport.Emit(new(DiscordPresenceNotificationKind.PresenceAcknowledged, requested with { PartySize = 2 }));
        transport.Emit(new(DiscordPresenceNotificationKind.PresenceAcknowledged, requested with
        {
            StartedAtUtc = requested.StartedAtUtc!.Value.AddSeconds(1)
        }));
        fixture.Client.ProcessPendingNotifications();
        Assert.Null(fixture.Client.LastPublishedPresence);
        transport.Acknowledge(transport.SetPresenceCalls[0]);
        fixture.Client.ProcessPendingNotifications();
        Assert.Equal(3, fixture.Client.LastPublishedPresence?.PartySize);
    }

    private sealed class ClientFixture : IDisposable
    {
        private readonly string _tempPath = CreateTempDirectory();
        public DateTime Now { get; set; } = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);
        public DiagnosticLog Log { get; }
        public DiscordPresenceClient Client { get; }

        public ClientFixture(params FakeDiscordPresenceTransport[] transports)
        {
            var queue = new Queue<FakeDiscordPresenceTransport>(transports);
            Log = new DiagnosticLog(Path.Combine(_tempPath, "rpc.log"));
            Client = new DiscordPresenceClient(CreateOptions(), Log, _ => queue.Dequeue(), () => Now);
        }

        public void Dispose()
        {
            Client.Dispose();
            Log.Dispose();
            Directory.Delete(_tempPath, true);
        }
    }

    private static DiscordOptions CreateOptions() => new()
    {
        ClientId = "test-client-id"
    };

    private static RenderedPresence CreatePresence() => new(
        "details",
        "state",
        null,
        "small",
        [],
        null,
        CodexActivityKind.Ready,
        RunningCommandKind.Unknown,
        "");

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "DiscordPresenceClientTests_" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class FakeDiscordPresenceTransport : IDiscordPresenceTransport
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<DiscordPresenceNotification> _notifications = new();

        public bool AutoReady { get; init; } = true;

        public bool AutoAcknowledge { get; init; } = true;

        public int InitializeCalls { get; private set; }

        public bool Disposed { get; private set; }

        public bool InitializeResult { get; init; } = true;

        public bool ThrowOnSetPresence { get; init; }

        public bool ThrowOnClearPresence { get; init; }

        public List<RichPresence> SetPresenceCalls { get; } = [];

        public List<bool> ClearPresenceCalls { get; } = [];

        public bool Initialize()
        {
            InitializeCalls++;
            if (InitializeResult && AutoReady)
            {
                Emit(new(DiscordPresenceNotificationKind.Ready));
            }
            return InitializeResult;
        }

        public bool TryDequeueNotification(out DiscordPresenceNotification? notification) =>
            _notifications.TryDequeue(out notification);

        public void Emit(DiscordPresenceNotification notification) => _notifications.Enqueue(notification);

        public void Acknowledge(RichPresence? presence) => Emit(new(
            DiscordPresenceNotificationKind.PresenceAcknowledged,
            presence is null ? null : DiscordPresenceSnapshot.From(presence)));

        public void SetPresence(RichPresence presence)
        {
            if (ThrowOnSetPresence)
            {
                throw new InvalidOperationException("fake SetPresence failure");
            }

            SetPresenceCalls.Add(presence);
            if (AutoAcknowledge)
            {
                Acknowledge(presence);
            }
        }

        public void ClearPresence()
        {
            if (ThrowOnClearPresence)
            {
                throw new InvalidOperationException("fake ClearPresence failure");
            }

            ClearPresenceCalls.Add(true);
            if (AutoAcknowledge)
            {
                Acknowledge(null);
            }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
