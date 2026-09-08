using System.Text;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordPresencePayloadPolicyTests
{
    [Fact]
    public void Create_NormalizesTextAndDropsInvalidButtonsBeforeSending()
    {
        var rendered = new RenderedPresence(
            "details\r\n" + new string('d', 200),
            "state\t" + new string('s', 200),
            "large\ntext",
            "small\rtext",
            [
                new RenderedButton(new string('b', 40), "https://example.com/valid"),
                new RenderedButton("invalid", "javascript:alert(1)"),
                new RenderedButton("too long", "https://example.com/" + new string('u', 510))
            ],
            null,
            CodexActivityKind.Ready,
            RunningCommandKind.Unknown,
            "");

        var payload = DiscordRichPresenceBuilder.Create(new DiscordOptions(), rendered, "party-id");

        Assert.DoesNotContain('\r', payload.Details);
        Assert.DoesNotContain('\n', payload.Details);
        Assert.DoesNotContain('\t', payload.State);
        Assert.True(Encoding.UTF8.GetByteCount(payload.Details) <= DiscordPresencePayloadPolicy.MaxTextBytes);
        Assert.True(Encoding.UTF8.GetByteCount(payload.State) <= DiscordPresencePayloadPolicy.MaxTextBytes);
        Assert.Equal("large text", payload.Assets!.LargeImageText);
        Assert.Equal("small text", payload.Assets.SmallImageText);
        Assert.Single(payload.Buttons!);
        Assert.True(Encoding.UTF8.GetByteCount(payload.Buttons[0].Label) <= DiscordPresencePayloadPolicy.MaxButtonLabelBytes);
        Assert.Equal("https://example.com/valid", payload.Buttons[0].Url);
    }

    [Fact]
    public void Create_TruncatesUnicodeButtonLabelsAtTheUtf8Boundary()
    {
        var rendered = new RenderedPresence(
            "details",
            "state",
            null,
            "small",
            [new RenderedButton(new string('\u3042', 20), "https://example.com/valid")],
            null,
            CodexActivityKind.Ready,
            RunningCommandKind.Unknown,
            "");

        var payload = DiscordRichPresenceBuilder.Create(new DiscordOptions(), rendered, "party-id");

        Assert.Single(payload.Buttons!);
        Assert.True(Encoding.UTF8.GetByteCount(payload.Buttons[0].Label) <= DiscordPresencePayloadPolicy.MaxButtonLabelBytes);
        Assert.EndsWith("…", payload.Buttons[0].Label, StringComparison.Ordinal);
    }
}
