using DiscordRPC;

namespace CodexDiscordPresence.Tests;

public sealed class DiscordPartyBuilderTests
{
    [Theory]
    [InlineData(2)]
    [InlineData(5)]
    public void Create_UsesPartySizeForBothDiscordSizeAndMax(int partySize)
    {
        var party = DiscordPartyBuilder.Create(partySize, "stable-party-id");

        Assert.NotNull(party);
        Assert.Equal("stable-party-id", party!.ID);
        Assert.Equal(partySize, party.Size);
        Assert.Equal(partySize, party.Max);
    }

    [Fact]
    public void Create_ReturnsNullWhenPartySizeIsUnavailable()
    {
        Assert.Null(DiscordPartyBuilder.Create(null, "stable-party-id"));
        Assert.Null(DiscordPartyBuilder.Create(0, "stable-party-id"));
        Assert.Null(DiscordPartyBuilder.Create(1, "stable-party-id"));
    }
}
