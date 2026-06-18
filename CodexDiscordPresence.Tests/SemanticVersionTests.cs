using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class SemanticVersionTests
{
    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("1.2.3", "1.2.3")]
    public void TryParse_NormalizesVPrefix(string input, string expected)
    {
        Assert.True(SemanticVersion.TryParse(input, out var version));
        Assert.Equal(expected, version!.ToString());
    }

    [Fact]
    public void CompareTo_TreatsNewerPatchAsGreater()
    {
        Assert.True(Parse("1.0.1").CompareTo(Parse("1.0.0")) > 0);
    }

    [Fact]
    public void CompareTo_TreatsReleaseAsNewerThanPrerelease()
    {
        Assert.True(Parse("1.0.0").CompareTo(Parse("1.0.0-beta.1")) > 0);
    }

    private static SemanticVersion Parse(string value)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        return version!;
    }
}
