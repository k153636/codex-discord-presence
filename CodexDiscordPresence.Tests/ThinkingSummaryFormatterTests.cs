using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ThinkingSummaryFormatterTests
{
    [Fact]
    public void FormatForPresence_TruncatesLongSummaryToOneLineLimit()
    {
        var summary = new string('x', 120);

        var formatted = ThinkingSummaryFormatter.FormatForPresence(summary);

        Assert.NotNull(formatted);
        Assert.Equal(96, formatted!.Length);
        Assert.EndsWith("…", formatted, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatForCurrentReasoning_PreservesSummaryAfterOperationCompleted()
    {
        var formatted = ThinkingSummaryFormatter.FormatForCurrentReasoning(
            "Previous reasoning summary",
            CodexActivityEventKind.OperationCompleted);

        Assert.Equal("Previous reasoning summary", formatted);
    }

    [Fact]
    public void FormatForCurrentReasoning_ClearsSummaryWhenNewOperationStarts()
    {
        var formatted = ThinkingSummaryFormatter.FormatForCurrentReasoning(
            "Previous reasoning summary",
            CodexActivityEventKind.OperationStarted);

        Assert.Null(formatted);
    }
}
