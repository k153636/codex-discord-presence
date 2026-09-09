using System.Text.Json;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class TokenUsageProviderTests
{
    [Fact]
    public void GetSnapshot_UsesProjectMatchedSessionAndKeepsExactSessionTotals()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var projectPath = @"E:\tool\discord-presence-for-codex";
            var otherProjectPath = Path.Combine(Path.GetTempPath(), "OtherProject_" + Guid.NewGuid());
            var projectSession = new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"project-session\",\"cwd\":\"{EscapeJson(projectPath)}\",\"model\":\"gpt-5.4-mini\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":60,\"cached_input_tokens\":10,\"output_tokens\":30,\"reasoning_output_tokens\":0,\"total_tokens\":90}},\"last_token_usage\":{{\"input_tokens\":60,\"cached_input_tokens\":10,\"output_tokens\":30,\"reasoning_output_tokens\":0,\"total_tokens\":90}},\"model_context_window\":258400}}}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:02.000Z\",\"type\":\"turn_context\",\"payload\":{{\"cwd\":\"{EscapeJson(projectPath)}\",\"model\":\"gpt-5.5\",\"collaboration_mode\":{{\"settings\":{{\"model\":\"gpt-5.5\"}}}}}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:03.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":120,\"cached_input_tokens\":20,\"output_tokens\":70,\"reasoning_output_tokens\":0,\"total_tokens\":190}},\"last_token_usage\":{{\"input_tokens\":60,\"cached_input_tokens\":10,\"output_tokens\":40,\"reasoning_output_tokens\":0,\"total_tokens\":100}},\"model_context_window\":258400}}}}}}"
            };

            WriteSession(tempHome, "project.jsonl", projectSession);
            WriteSession(tempHome, "other.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"other-session\",\"cwd\":\"{EscapeJson(otherProjectPath)}\",\"model\":\"gpt-5.5\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":999,\"cached_input_tokens\":999,\"output_tokens\":999,\"reasoning_output_tokens\":0,\"total_tokens\":1998}},\"last_token_usage\":{{\"input_tokens\":999,\"cached_input_tokens\":999,\"output_tokens\":999,\"reasoning_output_tokens\":0,\"total_tokens\":1998}},\"model_context_window\":258400}}}}}}"
            });

            SetSessionWriteTime(tempHome, "project.jsonl", DateTime.UtcNow);
            SetSessionWriteTime(tempHome, "other.jsonl", DateTime.UtcNow.AddMinutes(-10));

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(projectPath);

            Assert.Equal(190L, snapshot.TotalTokens);
            Assert.NotNull(snapshot.EstimatedCostUsd);
            Assert.True(snapshot.EstimatedCostUsd > 0);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_IgnoresTokenUsageFromOtherProjects()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var currentProjectPath = @"E:\tool\discord-presence-for-codex";
            var otherProjectPath = Path.Combine(Path.GetTempPath(), "OtherProject_" + Guid.NewGuid());

            WriteSession(tempHome, "other.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"other-session\",\"cwd\":\"{EscapeJson(otherProjectPath)}\",\"model\":\"gpt-5.5\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":300,\"cached_input_tokens\":0,\"output_tokens\":200,\"reasoning_output_tokens\":0,\"total_tokens\":500}},\"last_token_usage\":{{\"input_tokens\":300,\"cached_input_tokens\":0,\"output_tokens\":200,\"reasoning_output_tokens\":0,\"total_tokens\":500}},\"model_context_window\":258400}}}}}}"
            });

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(currentProjectPath);

            Assert.Null(snapshot.TotalTokens);
            Assert.Null(snapshot.EstimatedCostUsd);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_KeepsCostStableWhenSessionModelChanges()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var projectPath = @"E:\tool\discord-presence-for-codex";
            WriteSession(tempHome, "session.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"session\",\"cwd\":\"{EscapeJson(projectPath)}\",\"model\":\"gpt-5.5\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":100,\"cached_input_tokens\":0,\"output_tokens\":0,\"reasoning_output_tokens\":0,\"total_tokens\":100}},\"last_token_usage\":{{\"input_tokens\":100,\"cached_input_tokens\":0,\"output_tokens\":0,\"reasoning_output_tokens\":0,\"total_tokens\":100}},\"model_context_window\":258400}}}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:02.000Z\",\"type\":\"turn_context\",\"payload\":{{\"cwd\":\"{EscapeJson(projectPath)}\",\"model\":\"gpt-5.4-mini\",\"collaboration_mode\":{{\"settings\":{{\"model\":\"gpt-5.4-mini\"}}}}}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:03.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":200,\"cached_input_tokens\":0,\"output_tokens\":0,\"reasoning_output_tokens\":0,\"total_tokens\":200}},\"last_token_usage\":{{\"input_tokens\":100,\"cached_input_tokens\":0,\"output_tokens\":0,\"reasoning_output_tokens\":0,\"total_tokens\":100}},\"model_context_window\":258400}}}}}}"
            });

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(projectPath);

            Assert.Equal(200L, snapshot.TotalTokens);
            Assert.Equal(0.001m, snapshot.EstimatedCostUsd);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_LeavesCostUnsetWhenModelPricingIsUnknown()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var projectPath = @"E:\tool\discord-presence-for-codex";
            WriteSession(tempHome, "session.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"session\",\"cwd\":\"{EscapeJson(projectPath)}\",\"model\":\"unknown-model-x\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":5,\"reasoning_output_tokens\":0,\"total_tokens\":15}},\"last_token_usage\":{{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":5,\"reasoning_output_tokens\":0,\"total_tokens\":15}},\"model_context_window\":258400}}}}}}"
            });

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(projectPath);

            Assert.Equal(15L, snapshot.TotalTokens);
            Assert.NotNull(snapshot.EstimatedCostUsd);
            Assert.True(snapshot.EstimatedCostUsd > 0);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_UsesFallbackModelNameWhenSessionModelIsMissing()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var projectPath = @"E:\tool\discord-presence-for-codex";
            WriteSession(tempHome, "session.jsonl", new[] {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"id\":\"session\",\"cwd\":\"{EscapeJson(projectPath)}\"}}}}",
                $"{{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"token_count\",\"info\":{{\"total_token_usage\":{{\"input_tokens\":1000,\"cached_input_tokens\":0,\"output_tokens\":500,\"reasoning_output_tokens\":0,\"total_tokens\":1500}},\"last_token_usage\":{{\"input_tokens\":1000,\"cached_input_tokens\":0,\"output_tokens\":500,\"reasoning_output_tokens\":0,\"total_tokens\":1500}},\"model_context_window\":258400}}}}}}"
            });

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(projectPath, "gpt-5.4-mini");

            Assert.Equal(1500L, snapshot.TotalTokens);
            Assert.NotNull(snapshot.EstimatedCostUsd);
            Assert.True(snapshot.EstimatedCostUsd > 0);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_WithoutSessionScan_DefersTokenRead()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var projectPath = @"E:\tool\discord-presence-for-codex";
            WriteSession(tempHome, "session.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"session_meta\",\"payload\":{{\"cwd\":\"{EscapeJson(projectPath)}\"}}}}",
                "{\"timestamp\":\"2026-06-17T13:00:01.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":10,\"cached_input_tokens\":0,\"output_tokens\":5,\"reasoning_output_tokens\":0,\"total_tokens\":15}}}}"
            });

            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true });

            var snapshot = provider.GetSnapshot(
                projectPath,
                includeSessionScan: false);

            Assert.Null(snapshot.TotalTokens);
            Assert.Null(snapshot.EstimatedCostUsd);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void ResolveBillingType_MapsChatGptAccountToSubscWithoutUsingPlanName()
    {
        using var document = JsonDocument.Parse("{\"result\":{\"account\":{\"type\":\"chatgpt\",\"planType\":\"plus\"}}}");

        Assert.Equal("subsc", CodexAccountBillingTypeProvider.ResolveBillingType(document.RootElement));
    }

    [Fact]
    public void ResolveBillingType_MapsApiKeyAccountToApi()
    {
        using var document = JsonDocument.Parse("{\"result\":{\"account\":{\"type\":\"apiKey\"}}}");

        Assert.Equal("API", CodexAccountBillingTypeProvider.ResolveBillingType(document.RootElement));
    }

    [Fact]
    public void ResolveFiveHourRateLimit_ReadsPrimaryUsageAndResetTime()
    {
        var resetAt = DateTimeOffset.UtcNow.AddHours(3).AddMinutes(2);
        using var document = JsonDocument.Parse($"{{\"result\":{{\"rateLimits\":{{\"primary\":{{\"usedPercent\":25,\"windowDurationMins\":300,\"resetsAt\":{resetAt.ToUnixTimeSeconds()}}}}}}}}}");

        var snapshot = CodexAccountBillingTypeProvider.ResolveFiveHourRateLimit(document.RootElement);

        Assert.NotNull(snapshot);
        Assert.Equal(25, snapshot.UsedPercent);
        Assert.Equal(300, snapshot.WindowDurationMinutes);
        Assert.Equal(
            resetAt.ToUnixTimeSeconds(),
            new DateTimeOffset(snapshot.ResetAtUtc).ToUnixTimeSeconds());
    }

    [Fact]
    public void ResolveFiveHourRateLimit_IgnoresNonFiveHourWindow()
    {
        using var document = JsonDocument.Parse("{\"result\":{\"rateLimits\":{\"primary\":{\"usedPercent\":25,\"windowDurationMins\":60,\"resetsAt\":1780000000}}}}");

        Assert.Null(CodexAccountBillingTypeProvider.ResolveFiveHourRateLimit(document.RootElement));
    }

    [Fact]
    public void GetSnapshot_UsesDetectedBillingTypeWhenNoOverrideIsConfigured()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true },
                new StubBillingTypeProvider("subsc"));

            var snapshot = provider.GetSnapshot(includeSessionScan: false);

            Assert.Equal("subsc", snapshot.BillingType);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    [Fact]
    public void GetSnapshot_UsesDetectedFiveHourRateLimit()
    {
        var tempHome = CreateTempCodexHome();
        try
        {
            var resetAt = DateTime.UtcNow.AddHours(3);
            var provider = new TokenUsageProvider(
                new CodexDetectionOptions { HomePath = tempHome, ModelEnvironmentVariables = [] },
                new TokenUsageOptions { Enabled = true },
                new StubBillingTypeProvider("subsc"),
                new StubRateLimitProvider(new RateLimitSnapshot(25, 300, resetAt)));

            var snapshot = provider.GetSnapshot(includeSessionScan: false);

            Assert.Equal("subsc", snapshot.BillingType);
            Assert.Equal(25, snapshot.RateLimit?.UsedPercent);
            Assert.Equal(300, snapshot.RateLimit?.WindowDurationMinutes);
            Assert.Equal(resetAt, snapshot.RateLimit?.ResetAtUtc);
        }
        finally
        {
            Directory.Delete(tempHome, true);
        }
    }

    private static string CreateTempCodexHome()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexTokenTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempPath, "sessions"));
        return tempPath;
    }

    private static void WriteSession(string tempHome, string fileName, IEnumerable<string> lines)
    {
        File.WriteAllLines(Path.Combine(tempHome, "sessions", fileName), lines);
    }

    private static void SetSessionWriteTime(string tempHome, string fileName, DateTime timestamp)
    {
        File.SetLastWriteTimeUtc(Path.Combine(tempHome, "sessions", fileName), timestamp);
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    private sealed class StubBillingTypeProvider(string billingType) : IBillingTypeProvider
    {
        public string? GetBillingType(CancellationToken cancellationToken = default) => billingType;
    }

    private sealed class StubRateLimitProvider(RateLimitSnapshot rateLimit) : IRateLimitProvider
    {
        public RateLimitSnapshot? GetRateLimit(CancellationToken cancellationToken = default) => rateLimit;
    }
}
