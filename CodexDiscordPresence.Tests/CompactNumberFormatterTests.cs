using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CompactNumberFormatterTests
{
    [Theory]
    [InlineData(0L, "0")]
    [InlineData(999L, "999")]
    [InlineData(1_000L, "1K")]
    [InlineData(1_234L, "1.2K")]
    [InlineData(12_400L, "12.4K")]
    [InlineData(999_499L, "999.5K")]
    [InlineData(999_950L, "1M")]
    [InlineData(1_000_000L, "1M")]
    [InlineData(1_250_000L, "1.3M")]
    [InlineData(1_000_000_000L, "1B")]
    [InlineData(-12_400L, "-12.4K")]
    public void Format_UsesCompactInvariantUnits(long value, string expected)
    {
        Assert.Equal(expected, CompactNumberFormatter.Format(value));
    }

    [Fact]
    public void Format_LongMinValue_DoesNotOverflow()
    {
        Assert.Equal("-9223372T", CompactNumberFormatter.Format(long.MinValue));
    }

    [Theory]
    [InlineData(12_400L, "12.4K Token")]
    [InlineData(87_400_000L, "87.4M Token")]
    public void Render_UsesCompactFormatterForTokenPlaceholder(long totalTokens, string expected)
    {
        var renderer = new PresenceTemplateRenderer();
        var now = DateTime.UtcNow;
        var context = new PresenceContext(
            "gpt-5.6-luna",
            new CodexProcessSnapshot(true, "codex", false),
            new ProjectSnapshot("Project", @"E:\Project", null, null, 1, 1, 1, []),
            new GitSnapshot(false, 0, null),
            new SessionSnapshot(now, TimeSpan.Zero),
            new TokenUsageSnapshot(totalTokens, null));

        var presence = renderer.Render(
            new PresenceTemplateOptions { Details = "{Tokens}" },
            context);

        Assert.Equal(expected, presence.Details);
    }
}
