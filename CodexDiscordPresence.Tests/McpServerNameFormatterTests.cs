using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class McpServerNameFormatterTests
{
    [Theory]
    [InlineData("mcp__chrome_devtools__take_screenshot", "chrome-devtools")]
    [InlineData("mcp__filesystem__read_file", "filesystem")]
    [InlineData("chrome-devtools/take_screenshot", "chrome-devtools")]
    public void ExtractServerName_ReturnsServerSegment(string rawToolName, string expectedServerName)
    {
        Assert.Equal(expectedServerName, McpServerNameFormatter.Format(McpServerNameFormatter.ExtractServerName(rawToolName)));
    }

    [Fact]
    public void Format_TruncatesLongServerNameForCompactPresence()
    {
        var result = McpServerNameFormatter.Format("very-long-mcp-server-name");

        Assert.Equal(16, result.Length);
        Assert.EndsWith("…", result, StringComparison.Ordinal);
    }
}
