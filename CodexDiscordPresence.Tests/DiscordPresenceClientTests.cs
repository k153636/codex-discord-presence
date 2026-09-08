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
        public bool InitializeResult { get; init; } = true;

        public bool ThrowOnSetPresence { get; init; }

        public List<RichPresence> SetPresenceCalls { get; } = [];

        public List<bool> ClearPresenceCalls { get; } = [];

        public bool Initialize() => InitializeResult;

        public void SetPresence(RichPresence presence)
        {
            if (ThrowOnSetPresence)
            {
                throw new InvalidOperationException("fake SetPresence failure");
            }

            SetPresenceCalls.Add(presence);
        }

        public void ClearPresence()
        {
            ClearPresenceCalls.Add(true);
        }

        public void Dispose()
        {
        }
    }
}
