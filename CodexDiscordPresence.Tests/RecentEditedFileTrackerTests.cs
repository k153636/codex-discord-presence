using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class RecentEditedFileTrackerTests
{
    [Fact]
    public void GetRecentEditedFiles_KeepsLastEditedFileUntilProjectChanges()
    {
        var now = DateTime.UtcNow;
        var tracker = new RecentEditedFileTracker(() => now);
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexRecentEditedFileTrackerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "Edit.cs");
        File.WriteAllText(tempFile, "test");
        File.SetLastWriteTimeUtc(tempFile, now);

        try
        {
            var snapshot = new ProjectSnapshot(
                "Project",
                tempDir,
                Path.GetFileName(tempFile),
                tempFile,
                1,
                1,
                1,
                [
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, now)
                ]);

            var first = tracker.GetRecentEditedFiles(snapshot);
            Assert.Single(first);

            now = now.AddSeconds(10);
            var second = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "Project",
                tempDir,
                Path.GetFileName(tempFile),
                tempFile,
                1,
                1,
                1,
                [
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, now)
                ]));

            Assert.Single(second);
            Assert.Equal(first[0].Path, second[0].Path);

            now = now.AddSeconds(20);
            var third = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "Project",
                tempDir,
                Path.GetFileName(tempFile),
                tempFile,
                1,
                1,
                1,
                [
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, now)
                ]));

            Assert.Single(third);
            Assert.Equal(first[0].Path, third[0].Path);

            var nextDir = Path.Combine(Path.GetTempPath(), "CodexRecentEditedFileTrackerTests_" + Guid.NewGuid());
            Directory.CreateDirectory(nextDir);
            var nextFile = Path.Combine(nextDir, "Next.cs");
            File.WriteAllText(nextFile, "next");
            File.SetLastWriteTimeUtc(nextFile, now.AddSeconds(1));

            var afterProjectSwitch = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "NextProject",
                nextDir,
                Path.GetFileName(nextFile),
                nextFile,
                1,
                1,
                1,
                [
                    new RecentProjectFileSnapshot(Path.GetFileName(nextFile), nextFile, now.AddSeconds(1))
                ]));

            Assert.Single(afterProjectSwitch);
            Assert.Equal(nextFile, afterProjectSwitch[0].Path);

            Directory.Delete(nextDir, true);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
