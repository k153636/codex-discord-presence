using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class ProjectInspectorTests
{
    [Fact]
    public void GetSnapshot_IgnoresConfiguredFilePatterns()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexProjectInspectorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            var codeFile = Path.Combine(tempPath, "Feature.cs");
            var logFile = Path.Combine(tempPath, "presence-test.log");
            File.WriteAllText(codeFile, "code");
            File.WriteAllText(logFile, "log");
            File.SetLastWriteTimeUtc(codeFile, DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(logFile, DateTime.UtcNow);

            var inspector = new ProjectInspector(new ProjectOptions
            {
                Path = tempPath,
                IgnoredFilePatterns = ["*.log"]
            });

            var snapshot = inspector.GetSnapshot();

            Assert.Equal("Feature.cs", snapshot.RecentFileName);
            Assert.Equal(1, snapshot.ScannedFileCount);
            Assert.Equal(1, snapshot.TotalLineCount);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_CountsIncludedFilesAndLines()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexProjectInspectorTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempPath);

        try
        {
            File.WriteAllText(Path.Combine(tempPath, "One.cs"), "a\nb\n");
            File.WriteAllText(Path.Combine(tempPath, "Two.md"), "a\nb\nc");

            var inspector = new ProjectInspector(new ProjectOptions
            {
                Path = tempPath,
                IgnoredFilePatterns = []
            });

            var snapshot = inspector.GetSnapshot();

            Assert.Equal(2, snapshot.ScannedFileCount);
            Assert.Equal(5, snapshot.TotalLineCount);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
