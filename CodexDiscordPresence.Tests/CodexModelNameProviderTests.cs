using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexModelNameProviderTests
{
    [Fact]
    public void GetSnapshot_ConfigModelWinsForImmediateUiSwitch()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "config.toml"), "model = \"gpt-5-codex-high\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"session_meta\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\",\"model\":\"gpt-5-codex-low\"}}"
            });
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "sessions", "session.jsonl"), DateTime.UtcNow.AddMinutes(-5));
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "config.toml"), DateTime.UtcNow);

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\discord-presence-for-codex");

            Assert.Equal("gpt-5-codex-high", snapshot.SelectedUiModel);
            Assert.Equal("gpt-5-codex-low", snapshot.LastUsedSessionModel);
            Assert.Equal("gpt-5-codex-high", snapshot.FinalDisplayedModel);
            Assert.Equal("selected-ui", snapshot.Source);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_ProjectSessionWinsWhenConfigIsStale()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "config.toml"), "model = \"gpt-5.4-mini\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\",\"model\":\"gpt-5.5\",\"collaboration_mode\":{\"settings\":{\"model\":\"gpt-5.5\"}}}}"
            });
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "config.toml"), DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "sessions", "session.jsonl"), DateTime.UtcNow);

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\discord-presence-for-codex");

            Assert.Equal("gpt-5.4-mini", snapshot.SelectedUiModel);
            Assert.Equal("gpt-5.5", snapshot.LastUsedSessionModel);
            Assert.Equal("gpt-5.5", snapshot.FinalDisplayedModel);
            Assert.Equal("project-session", snapshot.Source);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_WhenAutoDetectDisabled_UsesFallbackModel()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "config.toml"), "model = \"gpt-5-codex-high\"\n");
            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = false, ModelName = "ManualModel" });

            var snapshot = provider.GetSnapshot(@"E:\tool\discord-presence-for-codex");

            Assert.Equal("gpt-5-codex-high", snapshot.SelectedUiModel);
            Assert.Equal("ManualModel", snapshot.FinalDisplayedModel);
            Assert.Equal("fallback", snapshot.Source);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    private static string CreateTempCodexHome()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexModelTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempPath, "sessions"));
        return tempPath;
    }

    private static void WriteSession(string tempPath, string fileName, string[] lines)
    {
        File.WriteAllLines(Path.Combine(tempPath, "sessions", fileName), lines);
    }
}
