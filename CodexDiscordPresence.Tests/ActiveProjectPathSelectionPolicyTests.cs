using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ActiveProjectPathSelectionPolicyTests
{
    [Fact]
    public void Select_PrefersFocusedProjectPath()
    {
        var testRoot = Path.Combine(Path.GetTempPath(), "CodexActiveProjectSelectionTests_" + Guid.NewGuid());
        var current = Path.Combine(testRoot, "CurrentProject");
        var focused = Path.Combine(testRoot, "FocusedProject");
        Directory.CreateDirectory(focused);
        var codex = new CodexProcessSnapshot(true, "codex", true)
        {
            ObservedProjectPath = Path.Combine(testRoot, "ObservedProject"),
            LastObservedAt = DateTime.UtcNow
        };
        var cli = new CodexProcessSnapshot(false, null, false);

        try
        {
            var selected = ActiveProjectPathSelectionPolicy.Select(current, focused, codex, cli);

            Assert.Equal(Path.GetFullPath(focused), selected);
        }
        finally
        {
            Directory.Delete(testRoot, true);
        }
    }

    [Fact]
    public void TryNormalizeFocusedProjectPath_RejectedForBroadContainerPath()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        var accepted = ActiveProjectPathSelectionPolicy.TryNormalizeFocusedProjectPath(
            userProfile,
            out var normalized,
            out var reason);

        Assert.False(accepted);
        Assert.Equal("", normalized);
        Assert.NotEmpty(reason);
    }

    [Fact]
    public void TryNormalizeFocusedProjectPath_AcceptsWorkspaceLikeFolder()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexActiveProjectPathTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            var accepted = ActiveProjectPathSelectionPolicy.TryNormalizeFocusedProjectPath(
                tempPath,
                out var normalized,
                out var reason);

            Assert.True(accepted);
            Assert.Equal(Path.GetFullPath(tempPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), normalized);
            Assert.Equal("", reason);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
