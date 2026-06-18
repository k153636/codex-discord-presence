using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class RecentEditedFileTrackerTests
{
    [Fact]
    public void GetRecentEditedFiles_KeepsLastEditedFileForShortRetentionWindow()
    {
        var now = DateTime.UtcNow;
        var currentTime = now;
        var tracker = new RecentEditedFileTracker(TimeSpan.FromSeconds(15), () => currentTime);
        var tempFile = Path.Combine(Path.GetTempPath(), "CodexRecentEditedFileTrackerTests_" + Guid.NewGuid(), "Edit.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(tempFile)!);
        File.WriteAllText(tempFile, "test");
        File.SetLastWriteTimeUtc(tempFile, now);

        try
        {
            var snapshot = new ProjectSnapshot(
                "Project",
                Path.GetDirectoryName(tempFile)!,
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

            currentTime = now.AddSeconds(10);
            var second = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "Project",
                Path.GetDirectoryName(tempFile)!,
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

            currentTime = now.AddSeconds(20);
            var third = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "Project",
                Path.GetDirectoryName(tempFile)!,
                Path.GetFileName(tempFile),
                tempFile,
                1,
                1,
                1,
                [
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, now)
                ]));

            Assert.Empty(third);
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(tempFile)!, true);
        }
    }
}
