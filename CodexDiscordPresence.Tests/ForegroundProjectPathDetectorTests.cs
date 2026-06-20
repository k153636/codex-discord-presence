using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ForegroundProjectPathDetectorTests
{
    [Fact]
    public void TryResolveProjectPathFromCommandLine_ReturnsDirectoryArgument()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexForegroundProjectTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            var commandLine = $"\"C:\\Program Files\\Code\\Code.exe\" \"{tempPath}\"";

            var result = ForegroundProjectPathParser.TryResolveProjectPathFromCommandLine(commandLine);

            Assert.Equal(Path.GetFullPath(tempPath), result);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void TryResolveProjectPathFromCommandLine_ReturnsParentForFileArgument()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexForegroundProjectTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            var solutionPath = Path.Combine(tempPath, "App.sln");
            File.WriteAllText(solutionPath, "");
            var commandLine = $"\"devenv.exe\" \"{solutionPath}\"";

            var result = ForegroundProjectPathParser.TryResolveProjectPathFromCommandLine(commandLine);

            Assert.Equal(Path.GetFullPath(tempPath), result);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void TryResolveProjectPathFromCommandLine_IgnoresExecutableOnlyCommandLine()
    {
        var result = ForegroundProjectPathParser.TryResolveProjectPathFromCommandLine("\"Code.exe\"");

        Assert.Null(result);
    }

    [Fact]
    public void GetFocusedProjectPath_IgnoresUnrelatedForegroundProcesses()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexForegroundProjectTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            var detector = new ForegroundProjectPathDetector(
                () => new nint(1234),
                _ => $"\"{tempPath}\"",
                _ => "chrome");

            var result = detector.GetFocusedProjectPath();

            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
