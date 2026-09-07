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
    public void GetSnapshot_ProjectSession_FormatsReasoningEffortAndEffectiveFastSpeed()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(
                Path.Combine(tempPath, "config.toml"),
                "model = \"gpt-5.4-mini\"\nmodel_reasoning_effort = \"medium\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"thread_settings_applied\",\"thread_settings\":{\"model\":\"gpt-5.6-luna\",\"reasoning_effort\":\"max\",\"service_tier\":\"priority\"}}}",
                "{\"timestamp\":\"2026-06-17T13:01:00.000Z\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"E:\\\\tool\\\\codex-discord-RPC\",\"model\":\"gpt-5.6-luna\",\"collaboration_mode\":{\"settings\":{\"model\":\"gpt-5.6-luna\",\"reasoning_effort\":\"max\"}}}}"
            });
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "config.toml"), DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "sessions", "session.jsonl"), DateTime.UtcNow);

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            Assert.Equal("gpt-5.6-luna", snapshot.FinalDisplayedModel);
            Assert.Equal("max", snapshot.ReasoningEffort);
            Assert.Equal("priority", snapshot.ServiceTier);
            Assert.Equal("gpt 5.6 luna max 1.5x", snapshot.DisplayLabel);
            Assert.Equal(snapshot.DisplayLabel, provider.GetModelName(@"E:\tool\codex-discord-RPC"));
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_ProjectSession_DefaultSpeedIsOmitted()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "config.toml"), "model = \"gpt-5.6-luna\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"E:\\\\tool\\\\codex-discord-RPC\",\"model\":\"gpt-5.6-luna\",\"reasoning_effort\":\"max\",\"service_tier\":\"default\"}}"
            });
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "config.toml"), DateTime.UtcNow.AddMinutes(-10));
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "sessions", "session.jsonl"), DateTime.UtcNow);

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            Assert.Equal("default", snapshot.ServiceTier);
            Assert.Equal("gpt 5.6 luna max", snapshot.DisplayLabel);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_ConfiguredFastModeWithoutEffectiveSessionTier_UsesConfiguredSpeed()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(
                Path.Combine(tempPath, "config.toml"),
                "model = \"gpt-5.6-luna\"\nmodel_reasoning_effort = \"max\"\nservice_tier = \"fast\"\n[features]\nfast_mode = true\n");

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            Assert.Equal("fast", snapshot.ServiceTier);
            Assert.Equal("gpt 5.6 luna max 1.5x", snapshot.DisplayLabel);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_CurrentSessionEventWinsWhenSessionFileWriteTimeIsStale()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            var now = DateTime.UtcNow;
            File.WriteAllText(
                Path.Combine(tempPath, "config.toml"),
                "model = \"gpt-5.6-luna\"\nmodel_reasoning_effort = \"medium\"\nservice_tier = \"default\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"thread_settings_applied\",\"thread_settings\":{{\"cwd\":\"E:\\\\tool\\\\codex-discord-RPC\",\"model\":\"gpt-5.6-luna\",\"reasoning_effort\":\"xhigh\",\"service_tier\":\"priority\"}}}}}}",
                $"{{\"timestamp\":\"{now:O}\",\"type\":\"turn_context\",\"payload\":{{\"cwd\":\"E:\\\\tool\\\\codex-discord-RPC\",\"model\":\"gpt-5.6-luna\"}}}}"
            });
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "config.toml"), now.AddMinutes(-1));
            File.SetLastWriteTimeUtc(Path.Combine(tempPath, "sessions", "session.jsonl"), now.AddMinutes(-10));

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            Assert.Equal("project-session", snapshot.Source);
            Assert.Equal("xhigh", snapshot.ReasoningEffort);
            Assert.Equal("priority", snapshot.ServiceTier);
            Assert.Equal("gpt 5.6 luna xhigh 1.5x", snapshot.DisplayLabel);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void GetSnapshot_ConfigSettingsAreRefreshedBetweenReads()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            var configPath = Path.Combine(tempPath, "config.toml");
            File.WriteAllText(
                configPath,
                "model = \"gpt-5.6-luna\"\nmodel_reasoning_effort = \"medium\"\nservice_tier = \"default\"\n");

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var first = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            File.WriteAllText(
                configPath,
                "model = \"gpt-5.6-luna\"\nmodel_reasoning_effort = \"high\"\nservice_tier = \"priority\"\n");
            File.SetLastWriteTimeUtc(configPath, DateTime.UtcNow.AddSeconds(1));

            var second = provider.GetSnapshot(@"E:\tool\codex-discord-RPC");

            Assert.Equal("gpt 5.6 luna medium", first.DisplayLabel);
            Assert.Equal("gpt 5.6 luna high 1.5x", second.DisplayLabel);
            Assert.Equal("high", second.ReasoningEffort);
            Assert.Equal("priority", second.ServiceTier);
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

    [Fact]
    public void GetSnapshot_WithoutSessionScan_UsesImmediateConfigEvidence()
    {
        var tempPath = CreateTempCodexHome();
        try
        {
            File.WriteAllText(Path.Combine(tempPath, "config.toml"), "model = \"gpt-5.6-luna\"\n");
            WriteSession(tempPath, "session.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"turn_context\",\"payload\":{\"cwd\":\"E:\\\\tool\\\\codex-discord-RPC\",\"model\":\"gpt-5.5\"}}"
            });

            var provider = new CodexModelNameProvider(
                new CodexDetectionOptions { HomePath = tempPath, ModelEnvironmentVariables = [] },
                new PresenceTemplateOptions { AutoDetectModelName = true, ModelName = "Codex" });

            var snapshot = provider.GetSnapshot(
                @"E:\tool\codex-discord-RPC",
                includeSessionScan: false);

            Assert.Equal("gpt-5.6-luna", snapshot.FinalDisplayedModel);
            Assert.Equal("selected-ui", snapshot.Source);
            Assert.Null(snapshot.LastUsedSessionModel);
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
