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
}
