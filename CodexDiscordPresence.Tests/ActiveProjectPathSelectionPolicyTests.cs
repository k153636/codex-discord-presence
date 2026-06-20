using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ActiveProjectPathSelectionPolicyTests
{
    [Fact]
    public void Select_PrefersFocusedProjectPath()
    {
        var current = @"E:\tool\discord-presence-for-codex";
        var focused = @"E:\tool\OtherProject";
        var codex = new CodexProcessSnapshot(true, "codex", true)
        {
            ObservedProjectPath = @"E:\tool\ThirdProject",
            LastObservedAt = DateTime.UtcNow
        };
        var cli = new CodexProcessSnapshot(false, null, false);

        var selected = ActiveProjectPathSelectionPolicy.Select(current, focused, codex, cli);

        Assert.Equal(Path.GetFullPath(focused), selected);
    }
}
