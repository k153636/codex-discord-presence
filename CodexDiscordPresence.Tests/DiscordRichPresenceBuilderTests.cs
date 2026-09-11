using DiscordRPC;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordRichPresenceBuilderTests
{
    [Fact]
    public void Create_ProducesThePayloadAndSnapshotUsedForTheDiscordPreview()
    {
        var startedAt = new DateTime(2026, 9, 7, 3, 4, 5, DateTimeKind.Utc);
        var rendered = new RenderedPresence(
            "details",
            "state",
            "large text",
            "small text",
            [
                new RenderedButton("one", "https://example.com/one"),
                new RenderedButton("two", "https://example.com/two"),
                new RenderedButton("three", "https://example.com/three")
            ],
            startedAt,
            CodexActivityKind.ApplyingEdits,
            RunningCommandKind.Unknown,
            "")
        {
            PartySize = 3
        };

        var options = new DiscordOptions();
        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "party-id");
        var snapshot = DiscordPresenceSnapshot.From(payload);

        Assert.Equal(rendered.Details, payload.Details);
        Assert.Equal(rendered.State, payload.State);
        Assert.Equal(
            DiscordAssetKeyResolver.ResolveLargeImageReference(options, rendered),
            payload.Assets!.LargeImageKey);
        Assert.Equal(rendered.LargeImageText, payload.Assets.LargeImageText);
        Assert.Equal(rendered.SmallImageText, payload.Assets.SmallImageText);
        Assert.Equal(startedAt, payload.Timestamps!.Start);
        Assert.Equal(3, payload.Party!.Size);
        Assert.Equal(3, payload.Party.Max);
        Assert.Equal(2, payload.Buttons!.Length);

        Assert.Equal(rendered.Details, snapshot.Details);
        Assert.Equal(rendered.State, snapshot.State);
        Assert.Equal(payload.Type, snapshot.ActivityType);
        Assert.Equal(payload.Assets.LargeImageKey, snapshot.LargeImageKey);
        Assert.Equal(payload.Assets.SmallImageKey, snapshot.SmallImageKey);
        Assert.Equal(startedAt, snapshot.StartedAtUtc);
        Assert.Equal(3, snapshot.PartySize);
        Assert.Equal(3, snapshot.PartyMax);
        Assert.Equal(["one", "two"], snapshot.Buttons.Select(button => button.Label).ToArray());
    }

    [Fact]
    public void Create_AntigravityPublishesOneOfOnePartyWithoutUsingTaskCount()
    {
        var rendered = new RenderedPresence(
            "details",
            "working",
            null,
            "small text",
            [],
            DateTime.UtcNow,
            CodexActivityKind.AnalyzingProject,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Antigravity,
            PartySize = 1
        };

        var options = new DiscordOptions
        {
            ClientId = "1548038167041671259",
            LargeImageKey = "rpc_antigravity",
            SmallImageKey = null,
            ExternalImageUrls = new(StringComparer.OrdinalIgnoreCase)
        };

        var payload = DiscordRichPresenceBuilder.Create(options, rendered, "antigravity-party");

        Assert.NotNull(payload.Party);
        Assert.Equal(1, payload.Party!.Size);
        Assert.Equal(1, payload.Party.Max);
        Assert.Equal("rpc_antigravity", payload.Assets!.LargeImageKey);
        Assert.Null(payload.Assets.SmallImageKey);
    }

    [Fact]
    public void Create_ProviderSwitchKeepsTheSharedRuntimeTimestamp()
    {
        var startedAt = new DateTime(2026, 9, 12, 3, 0, 0, DateTimeKind.Utc);
        var codexPresence = new RenderedPresence(
            "codex details",
            "Working",
            null,
            "",
            [],
            startedAt,
            CodexActivityKind.AnalyzingProject,
            RunningCommandKind.Unknown,
            "")
        {
            ProviderId = ProviderIds.Codex
        };
        var antigravityPresence = codexPresence with
        {
            Details = "antigravity details",
            ProviderId = ProviderIds.Antigravity
        };

        var codexPayload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions(),
            codexPresence,
            "codex-party");
        var antigravityPayload = DiscordRichPresenceBuilder.Create(
            new DiscordOptions { ClientId = "1548038167041671259" },
            antigravityPresence,
            "antigravity-party");

        Assert.Equal(codexPayload.Timestamps!.Start, antigravityPayload.Timestamps!.Start);
    }
}
