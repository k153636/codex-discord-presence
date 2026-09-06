using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class RecentEditedFileTrackerTests
{
    [Fact]
    public void GetRecentEditedFiles_ExpiresWhenFileIsNoLongerFreshOrTracked()
    {
        var now = DateTime.UtcNow;
        var editedAt = now;
        var tracker = new RecentEditedFileTracker(() => now);
        var tempDir = Path.Combine(Path.GetTempPath(), "CodexRecentEditedFileTrackerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);
        var tempFile = Path.Combine(tempDir, "Edit.cs");
        File.WriteAllText(tempFile, "test");
        File.SetLastWriteTimeUtc(tempFile, editedAt);

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
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, editedAt)
                ]);

            var first = tracker.GetRecentEditedFiles(snapshot, freshnessSeconds: 12);
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
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, editedAt)
                ]), freshnessSeconds: 12);

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
                    new RecentProjectFileSnapshot(Path.GetFileName(tempFile), tempFile, editedAt)
                ]), freshnessSeconds: 12);

            Assert.Empty(third);

            var deleted = tracker.GetRecentEditedFiles(new ProjectSnapshot(
                "Project",
                tempDir,
                null,
                null,
                0,
                0,
                0,
                []), freshnessSeconds: 12);

            Assert.Empty(deleted);

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
                ]), freshnessSeconds: 12);

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
