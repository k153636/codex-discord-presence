using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class GitInspectorTests
{
    [Fact]
    public void CountChangedFiles_SplitsLfPorcelainOutput()
    {
        var output = "?? CodexRpcEditTest.txt\n?? CodexRpcEditTest2.txt\n";

        var count = GitInspector.CountChangedFiles(output);

        Assert.Equal(2, count);
    }

    [Fact]
    public void CountChangedFiles_CountsRenameDestinationOnce()
    {
        var output = "R  OldName.cs -> NewName.cs\n M NewName.cs\n";

        var count = GitInspector.CountChangedFiles(output);

        Assert.Equal(1, count);
    }
}
