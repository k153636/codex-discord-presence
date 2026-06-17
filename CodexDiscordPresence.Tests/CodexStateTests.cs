using System;
using System.IO;
using Xunit;
using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public class CodexStateTests
{
    private string CreateTempSessionDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexTests_" + Guid.NewGuid().ToString());
        Directory.CreateDirectory(Path.Combine(tempPath, "sessions"));
        return tempPath;
    }

    private void WriteMockSessionLog(string tempPath, string fileName, string[] lines)
    {
        var sessionsDir = Path.Combine(tempPath, "sessions");
        var filePath = Path.Combine(sessionsDir, fileName);
        File.WriteAllLines(filePath, lines);
    }

    [Fact]
    public void Test_1_CodexProcessNotRunning_ReturnsOffline()
    {
        // 1. Codexプロセスなし → Offline (IsRunning = false, IsThinking = false)
        var options = new CodexDetectionOptions
        {
            ProcessNameContains = new[] { "non_existent_codex_process_xyz_123" },
            WindowTitleContains = new[] { "non_existent_codex_window_xyz_123" }
        };
        var presenceOptions = new PresenceTemplateOptions();
        var detector = new CodexProcessDetector(options, presenceOptions);

        var snapshot = detector.GetSnapshot();
        Assert.False(snapshot.IsRunning);
        Assert.False(snapshot.IsThinking);
    }

    [Fact]
    public void Test_2_NoTaskStartedInSession_ReturnsWaiting()
    {
        // 2. Codex起動中・最新sessionにtask_startedなし → waiting (IsThinking = false)
        var tempPath = CreateTempSessionDirectory();
        try
        {
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}"
            });

            var options = new CodexDetectionOptions { HomePath = tempPath };
            var presenceOptions = new PresenceTemplateOptions();
            var detector = new CodexProcessDetector(options, presenceOptions);

            var isThinking = detector.DetermineIfThinking();
            Assert.False(isThinking);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_3_LatestSessionHasTaskStarted_ReturnsThinking()
    {
        // 3. 最新sessionにtask_started追加 → Thinking (IsThinking = true)
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{nowStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var options = new CodexDetectionOptions { HomePath = tempPath };
            var presenceOptions = new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 };
            var detector = new CodexProcessDetector(options, presenceOptions);

            var isThinking = detector.DetermineIfThinking();
            Assert.True(isThinking);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_4_LatestSessionHasTaskComplete_ReturnsWaiting()
    {
        // 4. task_complete追加 → waiting (IsThinking = false)
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            var time1 = now.AddSeconds(-5).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var time2 = now.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{time1}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}",
                $"{{\"timestamp\":\"{time2}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_complete\",\"turn_id\":\"123\"}}}}"
            });

            var options = new CodexDetectionOptions { HomePath = tempPath };
            var presenceOptions = new PresenceTemplateOptions();
            var detector = new CodexProcessDetector(options, presenceOptions);

            var isThinking = detector.DetermineIfThinking();
            Assert.False(isThinking);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_5_TaskStartedStaleTimeout_ReturnsWaiting()
    {
        // 5. task_started後、ThinkingStaleTimeoutMinutesを超えた扱い → waiting (IsThinking = false)
        var tempPath = CreateTempSessionDirectory();
        try
        {
            // 11 minutes ago
            var staleTimeStr = DateTime.UtcNow.AddMinutes(-11).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{staleTimeStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var options = new CodexDetectionOptions { HomePath = tempPath };
            var presenceOptions = new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 };
            var detector = new CodexProcessDetector(options, presenceOptions);

            var isThinking = detector.DetermineIfThinking();
            Assert.False(isThinking);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_6_NewTaskStartedAfterStale_ReturnsThinking()
    {
        // 6. staleでwaitingに戻った後、新しいtask_started追加 → Thinking (IsThinking = true)
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var staleTimeStr = DateTime.UtcNow.AddMinutes(-15).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{staleTimeStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}",
                $"{{\"timestamp\":\"{nowStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"456\"}}}}"
            });

            var options = new CodexDetectionOptions { HomePath = tempPath };
            var presenceOptions = new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 };
            var detector = new CodexProcessDetector(options, presenceOptions);

            var isThinking = detector.DetermineIfThinking();
            Assert.True(isThinking);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }
}
