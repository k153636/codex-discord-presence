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
}
