namespace CodexDiscordPresence.Tests;

public sealed class DiagnosticLogTests
{
    [Fact]
    public void CreateAndWrite_PersistsLinesToFile()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "codex-discord-presence-log-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            using (var log = DiagnosticLog.Create(tempPath))
            {
                log.Info("hello");
                log.Warn("world");
            }

            var files = Directory.GetFiles(tempPath, "presence-*.log");
            Assert.Single(files);

            var content = File.ReadAllText(files[0]);
            Assert.Contains("[INFO] hello", content);
            Assert.Contains("[WARN] world", content);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void CreateAndWrite_RotatesWhenMaxSizeExceeded()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "codex-discord-presence-log-rotate-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempPath);

        try
        {
            using (var log = new DiagnosticLog(Path.Combine(tempPath, "presence.log"), 1))
            {
                var initialPath = log.Path;
                log.Info("a");
                var afterFirstWritePath = log.Path;
                log.Info("b");
                var afterSecondWritePath = log.Path;
                Assert.NotEqual(initialPath, afterFirstWritePath);
                Assert.NotEqual(afterFirstWritePath, afterSecondWritePath);
            }

            var files = Directory.GetFiles(tempPath, "presence*.log");
            Assert.True(files.Length >= 2);
            Assert.Contains(files, file => Path.GetFileName(file) == "presence.log");
            Assert.Contains(files, file => Path.GetFileName(file).StartsWith("presence-1", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
