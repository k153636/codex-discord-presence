namespace CodexDiscordPresence.Tests;

public sealed class DiscordPresenceSignatureTests
{
    [Fact]
    public void GetPayloadSignature_HiddenCommandChanges_DoNotRequestAnotherSend()
    {
        using var fixture = new Fixture();
        var original = CreatePresence();
        var changed = original with
        {
            RunningCommandName = "another command",
            RunningCommandKind = RunningCommandKind.Unknown,
            ActivityKind = CodexActivityKind.AnalyzingProject,
            IsThinking = true
        };

        var first = fixture.Client.GetPayloadSignature(original);
        var next = fixture.Client.GetPayloadSignature(changed);

        Assert.Equal(first, next);
        Assert.False(PresenceDispatchPolicy.ShouldSendPresence(next, first, false, false));
    }

    [Fact]
    public void GetPayloadSignature_ChangesBeyondUtf8Limit_DoNotRequestAnotherSend()
    {
        using var fixture = new Fixture();
        var original = CreatePresence() with { State = new string('あ', 60) + "first" };
        var changed = original with { State = new string('あ', 60) + "second" };

        Assert.Equal(fixture.Client.GetPayloadSignature(original), fixture.Client.GetPayloadSignature(changed));
    }

    [Fact]
    public void GetPayloadSignature_InvalidOrThirdButtons_DoNotRequestAnotherSend()
    {
        using var fixture = new Fixture();
        var original = CreatePresence() with
        {
            Buttons = [new("one", "https://example.com/1"), new("two", "https://example.com/2")]
        };
        var changed = original with
        {
            Buttons = [new("invalid", "not a URL"), .. original.Buttons, new("three", "https://example.com/3")]
        };

        Assert.Equal(fixture.Client.GetPayloadSignature(original), fixture.Client.GetPayloadSignature(changed));
    }

    [Fact]
    public void GetPayloadSignature_VisibleFieldsAndTimestampChanges_RequestAnotherSend()
    {
        using var fixture = new Fixture();
        var original = CreatePresence();
        var first = fixture.Client.GetPayloadSignature(original);
        RenderedPresence[] changes =
        [
            original with { State = "changed" },
            original with { Details = "changed" },
            original with { LargeImageText = "changed" },
            original with { StartedAt = original.StartedAt!.Value.AddSeconds(1) },
            original with { PartySize = 3 },
            original with { Buttons = [new("new", "https://example.com/new")] }
        ];

        foreach (var changed in changes)
        {
            var next = fixture.Client.GetPayloadSignature(changed);
            Assert.NotEqual(first, next);
            Assert.True(PresenceDispatchPolicy.ShouldSendPresence(next, first, false, false));
        }
    }

    [Fact]
    public void GetPayloadSignature_ResolvedExternalImageChanges_RequestAnotherSend()
    {
        using var fixture = new Fixture();
        var presence = CreatePresence();
        var first = fixture.Client.GetPayloadSignature(presence);
        fixture.Client.UpdateOptions(new DiscordOptions
        {
            ClientId = "123456789012345678",
            LargeImageKey = "fixed",
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase) { ["rpc_antigravity_cli"] = "https://example.com/new.gif" }
        });

        Assert.NotEqual(first, fixture.Client.GetPayloadSignature(presence));
        Assert.True(fixture.Client.NeedsPresenceRefresh);
    }

    [Fact]
    public void GetPayloadSignature_SubagentWorkChanges_RequestAnotherSend()
    {
        using var fixture = new Fixture();
        var first = CreatePresence() with
        {
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Editing])
        };
        var changed = first with
        {
            SubagentActivity = SubagentActivitySummary.Create(1, [SubagentWorkKind.Reading])
        };

        Assert.NotEqual(fixture.Client.GetPayloadSignature(first), fixture.Client.GetPayloadSignature(changed));
        Assert.NotEqual(fixture.Client.GetPayloadSignature(first), fixture.Client.GetPayloadSignature(CreatePresence()));
    }

    [Fact]
    public void GetPayloadSignature_UnchangedPayload_StillPermitsKeepAliveAndReconnectRefresh()
    {
        using var fixture = new Fixture();
        var signature = fixture.Client.GetPayloadSignature(CreatePresence());

        Assert.True(PresenceDispatchPolicy.ShouldSendPresence(signature, signature, true, false));
        Assert.True(PresenceDispatchPolicy.ShouldSendPresence(signature, signature, false, true));
    }

    private static RenderedPresence CreatePresence() => new(
        "details", "state", "large text", "small text", [],
        new DateTime(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc),
        CodexActivityKind.RunningCommand, RunningCommandKind.Build, "build")
    {
        ProviderId = ProviderIds.Antigravity
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "rpc-signature-" + Guid.NewGuid());
        private readonly DiagnosticLog _log;
        public DiscordPresenceClient Client { get; }

        public Fixture()
        {
            _log = new DiagnosticLog(Path.Combine(_directory, "rpc.log"));
            Client = new DiscordPresenceClient(new DiscordOptions
            {
                ClientId = "123456789012345678",
                LargeImageKey = "fixed",
                ActivityImageKeys = new(StringComparer.OrdinalIgnoreCase),
                RunningCommandImageKeys = new(StringComparer.OrdinalIgnoreCase),
                ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
            }, _log);
        }

        public void Dispose()
        {
            Client.Dispose();
            _log.Dispose();
            Directory.Delete(_directory, true);
        }
    }
}
