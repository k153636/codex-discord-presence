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
            .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
            .Select(button => new RpcButton { Label = button.Label, Url = button.Url })
            .Take(2)
            .ToArray();

        return new RichPresence
        {
            Details = presence.Details,
            State = presence.State,
            Assets = new Assets
            {
                LargeImageKey = DiscordAssetKeyResolver.ResolveLargeImageReference(options, presence),
                LargeImageText = presence.LargeImageText,
                SmallImageKey = DiscordAssetKeyResolver.ResolveImageReference(options, options.SmallImageKey),
                SmallImageText = presence.SmallImageText
            },
            Party = DiscordPartyBuilder.Create(presence.PartySize, partyId),
            Buttons = buttons.Length == 0 ? null : buttons,
            Timestamps = presence.StartedAt is null ? null : new Timestamps(presence.StartedAt.Value)
        };
    }
}
