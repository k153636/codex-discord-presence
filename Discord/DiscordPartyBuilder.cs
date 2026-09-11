using DiscordRPC;

namespace CodexDiscordPresence;

internal static class DiscordPartyBuilder
{
    public static Party? Create(int? partySize, string partyId, bool includeSolo = false)
    {
        if ((includeSolo ? partySize is not > 0 : partySize is not > 1) ||
            string.IsNullOrWhiteSpace(partyId))
        {
            return null;
        }

        var size = partySize.Value;
        return new Party
        {
            ID = partyId,
            Size = size,
            Max = size
        };
    }
}
