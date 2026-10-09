using DiscordRPC;
using RpcButton = DiscordRPC.Button;

namespace CodexDiscordPresence;

internal static class DiscordRichPresenceBuilder
{
    public static RichPresence Create(
        DiscordOptions options,
        RenderedPresence presence,
        string partyId)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presence);

        var buttons = presence.Buttons
            .Select(CreateButton)
            .Where(button => button is not null)
            .Cast<RpcButton>()
            .Take(2)
            .ToArray();
        var smallImage = DiscordSubagentSmallImagePolicy.Resolve(options, presence);

        return new RichPresence
        {
            Details = DiscordPresencePayloadPolicy.NormalizeText(presence.Details),
            State = DiscordPresencePayloadPolicy.NormalizeText(presence.State),
            Assets = new Assets
            {
                LargeImageKey = DiscordAssetKeyResolver.ResolveLargeImageReference(options, presence),
                LargeImageText = DiscordPresencePayloadPolicy.NormalizeOptionalText(presence.LargeImageText),
                SmallImageKey = smallImage.ImageReference,
                SmallImageText = DiscordPresencePayloadPolicy.NormalizeOptionalText(smallImage.Text)
            },
            Party = DiscordPartyBuilder.Create(
                presence.PartySize,
                partyId),
            Buttons = buttons.Length == 0 ? null : buttons,
            Timestamps = presence.StartedAt is null ? null : new Timestamps(presence.StartedAt.Value)
        };
    }

    private static RpcButton? CreateButton(RenderedButton button)
    {
        return DiscordPresencePayloadPolicy.TryNormalizeButton(button, out var normalized)
            ? new RpcButton { Label = normalized.Label, Url = normalized.Url }
            : null;
    }
}
