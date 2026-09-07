using DiscordRPC;

namespace CodexDiscordPresence;

public sealed record DiscordPresenceSnapshot(
    string? Details,
    string? State,
    string? LargeImageKey,
    string? LargeImageText,
    string? SmallImageKey,
    string? SmallImageText,
    DateTime? StartedAtUtc,
    int? PartySize,
    int? PartyMax,
    IReadOnlyList<RenderedButton> Buttons)
{
    public static DiscordPresenceSnapshot From(RichPresence presence)
    {
        ArgumentNullException.ThrowIfNull(presence);

        var buttons = presence.Buttons is null
            ? Array.Empty<RenderedButton>()
            : presence.Buttons
                .Where(button => !string.IsNullOrWhiteSpace(button.Label) && !string.IsNullOrWhiteSpace(button.Url))
                .Select(button => new RenderedButton(button.Label, button.Url))
                .ToArray();

        return new DiscordPresenceSnapshot(
            presence.Details,
            presence.State,
            presence.Assets?.LargeImageKey,
            presence.Assets?.LargeImageText,
            presence.Assets?.SmallImageKey,
            presence.Assets?.SmallImageText,
            presence.Timestamps?.Start,
            presence.Party?.Size,
            presence.Party?.Max,
            buttons);
    }
}
