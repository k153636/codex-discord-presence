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
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
