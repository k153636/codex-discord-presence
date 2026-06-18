using System;
using System.IO;
using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public class CodexStateTests
{
    private string CreateTempSessionDirectory()
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "CodexTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(tempPath, "sessions"));
        return tempPath;
    }

    private void WriteMockSessionLog(string tempPath, string fileName, string[] lines)
    {
        var sessionsDir = Path.Combine(tempPath, "sessions");
        var filePath = Path.Combine(sessionsDir, fileName);
        File.WriteAllLines(filePath, lines);
    }

    private void SetSessionWriteTime(string tempPath, string fileName, DateTime timestamp)
    {
        var filePath = Path.Combine(tempPath, "sessions", fileName);
        File.SetLastWriteTimeUtc(filePath, timestamp);
    }

    [Fact]
    public void Test_1_CodexProcessNotRunning_ReturnsOffline()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var options = new CodexDetectionOptions
            {
                HomePath = tempPath,
                ProcessNameContains = new[] { "non_existent_codex_process_xyz_123" },
                WindowTitleContains = new[] { "non_existent_codex_window_xyz_123" }
            };

            var detector = new CodexProcessDetector(options, new PresenceTemplateOptions());

            var snapshot = detector.GetSnapshot();

            Assert.False(snapshot.IsRunning);
            Assert.False(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.Offline, snapshot.ActivityKind);
            Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_2_NoTaskStartedInSession_ReturnsReady()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                "{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\"}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var isThinking = detector.DetermineIfThinking();
            var snapshot = detector.GetSnapshot();

            Assert.False(isThinking);
            Assert.Equal(CodexActivityKind.Ready, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_2b_ObservedProjectPath_UsesLatestSessionCwd()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var firstProject = @"E:\tool\ProjectOne";
            var secondProject = @"E:\tool\ProjectTwo";

            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"session_meta\",\"cwd\":\"{firstProject.Replace("\\", "\\\\")}\"}}}}"
            });
            WriteMockSessionLog(tempPath, "session2.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:10.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"turn_context\",\"cwd\":\"{secondProject.Replace("\\", "\\\\")}\"}}}}"
            });
            SetSessionWriteTime(tempPath, "session1.jsonl", DateTime.UtcNow.AddSeconds(-10));
            SetSessionWriteTime(tempPath, "session2.jsonl", DateTime.UtcNow);

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var observedProjectPath = detector.GetObservedProjectPath();

            Assert.Equal(secondProject, observedProjectPath);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_2c_ObservedProjectPath_IgnoresOlderMatchWhenNewerProjectExists()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var currentProject = @"E:\tool\ProjectOne";
            var switchedProject = @"E:\tool\ProjectTwo";

            WriteMockSessionLog(tempPath, "old.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:00.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"session_meta\",\"cwd\":\"{currentProject.Replace("\\", "\\\\")}\"}}}}"
            });
            WriteMockSessionLog(tempPath, "new.jsonl", new[]
            {
                $"{{\"timestamp\":\"2026-06-17T13:00:10.000Z\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"session_meta\",\"cwd\":\"{switchedProject.Replace("\\", "\\\\")}\"}}}}"
            });
            SetSessionWriteTime(tempPath, "old.jsonl", DateTime.UtcNow.AddSeconds(-20));
            SetSessionWriteTime(tempPath, "new.jsonl", DateTime.UtcNow);

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var observedProjectPath = detector.GetObservedProjectPath(currentProject);

            Assert.Equal(switchedProject, observedProjectPath);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_3_LatestSessionHasTaskStarted_ReturnsAnalyzingProject()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{nowStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 });

            var snapshot = detector.GetSnapshot();

            Assert.True(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.AnalyzingProject, snapshot.ActivityKind);
            Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_4_LatestSessionHasTaskComplete_ReturnsReady()
    {
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

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var snapshot = detector.GetSnapshot();

            Assert.False(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.Ready, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_5_TaskStartedStaleTimeout_ReturnsReady()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var staleTimeStr = DateTime.UtcNow.AddMinutes(-11).ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{staleTimeStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 });

            var snapshot = detector.GetSnapshot();

            Assert.False(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.Ready, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_6_NewTaskStartedAfterStale_ReturnsAnalyzingProject()
    {
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

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 });

            var snapshot = detector.GetSnapshot();

            Assert.True(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.AnalyzingProject, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_7_ProjectMatchedSessionWinsOverNewerOtherProject()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            var currentProject = Path.Combine(Path.GetTempPath(), "CodexCurrentProject");
            var otherProject = Path.Combine(Path.GetTempPath(), "CodexOtherProject");

            WriteMockSessionLog(tempPath, "current.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now.AddSeconds(-20):yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"session_meta\",\"cwd\":\"{EscapeJson(currentProject)}\"}}}}",
                $"{{\"timestamp\":\"{now.AddSeconds(-10):yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_complete\",\"turn_id\":\"current\"}}}}"
            });
            WriteMockSessionLog(tempPath, "other.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"session_meta\",\"cwd\":\"{EscapeJson(otherProject)}\"}}}}",
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"other\"}}}}"
            });
            SetSessionWriteTime(tempPath, "current.jsonl", now.AddSeconds(-5));
            SetSessionWriteTime(tempPath, "other.jsonl", now);

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var snapshot = detector.GetSnapshot(currentProject);

            Assert.False(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.Ready, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_8_NoProjectMetadataFallsBackToLatestSession()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var nowStr = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{nowStr}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 10 });

            var snapshot = detector.GetSnapshot(Path.Combine(Path.GetTempPath(), "AnyProject"));

            Assert.True(snapshot.IsThinking);
            Assert.Equal(CodexActivityKind.AnalyzingProject, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_9_PlanModeSession_ReturnsPlanningLowConfidence()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"turn_context\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\",\"collaboration_mode\":{{\"mode\":\"plan\",\"settings\":{{\"model\":\"gpt-5.5\"}}}}}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());
            var projectSnapshot = new ProjectSnapshot(
                "discord-presence-for-codex",
                @"E:\tool\discord-presence-for-codex",
                null,
                null,
                10,
                10,
                200,
                []);

            var snapshot = detector.GetSnapshot(@"E:\tool\discord-presence-for-codex", projectSnapshot, new GitSnapshot(false, 0, null));

            Assert.True(snapshot.IsRunning);
            Assert.Equal(CodexActivityKind.Planning, snapshot.ActivityKind);
            Assert.True(snapshot.IsThinking);
            Assert.Equal(ActivityConfidence.Low, snapshot.Confidence);
            Assert.Equal(ActivityProvenance.Observed, snapshot.ActivityProvenance);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_9b_GoalModeAliasNormalizesToPlan()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"turn_context\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\",\"collaboration_mode\":{{\"mode\":\"goalmode\",\"settings\":{{\"model\":\"gpt-5.5\"}}}}}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());
            var projectSnapshot = new ProjectSnapshot(
                "discord-presence-for-codex",
                @"E:\tool\discord-presence-for-codex",
                null,
                null,
                10,
                10,
                200,
                []);

            var snapshot = detector.GetSnapshot(@"E:\tool\discord-presence-for-codex", projectSnapshot, new GitSnapshot(false, 0, null));

            Assert.Equal("plan", snapshot.CollaborationMode);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_10_SingleRecentEdit_ReturnsApplyingEdits()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\"}}}}"
            });

            var projectRoot = Path.Combine(Path.GetTempPath(), "CodexSingleEditProject_" + Guid.NewGuid());
            Directory.CreateDirectory(projectRoot);

            try
            {
                var file = Path.Combine(projectRoot, "README.md");
                File.WriteAllText(file, "hello");
                File.SetLastWriteTimeUtc(file, now);

                var projectSnapshot = new ProjectSnapshot(
                    Path.GetFileName(projectRoot),
                    projectRoot,
                    "README.md",
                    file,
                    1,
                    1,
                    1,
                    [
                        new RecentProjectFileSnapshot("README.md", file, File.GetLastWriteTimeUtc(file))
                    ]);

                var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
                var snapshot = detector.GetSnapshot(projectRoot, projectSnapshot, new GitSnapshot(true, 12, null));

                Assert.True(snapshot.IsRunning);
                Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
                Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_10b_TaskStartedWithDiffButNoFreshEdit_ReturnsApplyingEditsSooner()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\"}}}}"
            });

            var projectRoot = Path.Combine(Path.GetTempPath(), "CodexSingleEditProject_" + Guid.NewGuid());
            Directory.CreateDirectory(projectRoot);

            try
            {
                var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
                var projectSnapshot = new ProjectSnapshot(
                    Path.GetFileName(projectRoot),
                    projectRoot,
                    null,
                    null,
                    1,
                    1,
                    1,
                    []);

                var snapshot = detector.GetSnapshot(
                    projectRoot,
                    projectSnapshot,
                    new GitSnapshot(true, 1, null),
                    CodexActivityKind.AnalyzingProject);

                Assert.True(snapshot.IsRunning);
                Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
                Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_11_MultiFileEdits_ReturnCoordinatingChanges()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\",\"cwd\":\"E:\\\\tool\\\\discord-presence-for-codex\"}}}}"
            });

            var projectRoot = Path.Combine(Path.GetTempPath(), "CodexEditingProject_" + Guid.NewGuid());
            Directory.CreateDirectory(projectRoot);

            try
            {
                var file1 = Path.Combine(projectRoot, "One.cs");
                var file2 = Path.Combine(projectRoot, "Two.cs");
                var file3 = Path.Combine(projectRoot, "Three.cs");
                var file4 = Path.Combine(projectRoot, "Four.cs");
                File.WriteAllText(file1, "1");
                File.WriteAllText(file2, "2");
                File.WriteAllText(file3, "3");
                File.WriteAllText(file4, "4");
                File.SetLastWriteTimeUtc(file1, now);
                File.SetLastWriteTimeUtc(file2, now.AddSeconds(-1));
                File.SetLastWriteTimeUtc(file3, now.AddSeconds(-2));
                File.SetLastWriteTimeUtc(file4, now.AddSeconds(-3));

                var projectSnapshot = new ProjectSnapshot(
                    Path.GetFileName(projectRoot),
                    projectRoot,
                    "One.cs",
                    file1,
                    4,
                    4,
                    42000,
                    [
                        new RecentProjectFileSnapshot("One.cs", file1, File.GetLastWriteTimeUtc(file1)),
                        new RecentProjectFileSnapshot("Two.cs", file2, File.GetLastWriteTimeUtc(file2)),
                        new RecentProjectFileSnapshot("Three.cs", file3, File.GetLastWriteTimeUtc(file3)),
                        new RecentProjectFileSnapshot("Four.cs", file4, File.GetLastWriteTimeUtc(file4))
                    ]);

                var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
                var snapshot = detector.GetSnapshot(projectRoot, projectSnapshot, new GitSnapshot(true, 4, null));

                Assert.True(snapshot.IsRunning);
                Assert.Equal(CodexActivityKind.CoordinatingChanges, snapshot.ActivityKind);
                Assert.True(snapshot.IsThinking);
                Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_12_CommandInSession_ReturnsRunningCommand()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now}\",\"type\":\"response_item\",\"payload\":{{\"type\":\"function_call\",\"name\":\"shell_command\",\"call_id\":\"call_123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions());

            var snapshot = detector.GetSnapshot();

            Assert.True(snapshot.IsRunning);
            Assert.Equal(CodexActivityKind.RunningCommand, snapshot.ActivityKind);
            Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_13_CommitMessageWithRefactorHint_ReturnsRefactoringLowConfidence()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
            var projectSnapshot = new ProjectSnapshot(
                "discord-presence-for-codex",
                @"E:\tool\discord-presence-for-codex",
                null,
                null,
                12,
                12,
                500,
                []);

            var snapshot = detector.GetSnapshot(
                @"E:\tool\discord-presence-for-codex",
                projectSnapshot,
                new GitSnapshot(true, 1, "refactor: split editor state from transport"));

            Assert.True(snapshot.IsRunning);
            Assert.Equal(CodexActivityKind.Refactoring, snapshot.ActivityKind);
            Assert.Equal(ActivityConfidence.Low, snapshot.Confidence);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_14_RefactorWordInSessionText_DoesNotForceRefactoring()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"text\":\"Let's refactor this later\"}}}}"
            });

            var projectRoot = Path.Combine(Path.GetTempPath(), "CodexEditingProject_" + Guid.NewGuid());
            Directory.CreateDirectory(projectRoot);

            try
            {
                var file1 = Path.Combine(projectRoot, "Alpha.cs");
                File.WriteAllText(file1, "1");
                File.SetLastWriteTimeUtc(file1, now);

                var projectSnapshot = new ProjectSnapshot(
                    Path.GetFileName(projectRoot),
                    projectRoot,
                    "Alpha.cs",
                    file1,
                    1,
                    1,
                    2,
                    [ new RecentProjectFileSnapshot("Alpha.cs", file1, File.GetLastWriteTimeUtc(file1)) ]);

                var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
                var snapshot = detector.GetSnapshot(projectRoot, projectSnapshot, new GitSnapshot(true, 2, null));

                Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
                Assert.Equal(ActivityConfidence.High, snapshot.Confidence);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_14b_SplitCommitMessage_DoesNotForceRefactoring()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var detector = new CodexProcessDetector(new CodexDetectionOptions { HomePath = tempPath }, new PresenceTemplateOptions { EditingFreshnessSeconds = 120 });
            var projectSnapshot = new ProjectSnapshot(
                "discord-presence-for-codex",
                @"E:\tool\discord-presence-for-codex",
                null,
                null,
                12,
                12,
                500,
                []);

            var snapshot = detector.GetSnapshot(
                @"E:\tool\discord-presence-for-codex",
                projectSnapshot,
                new GitSnapshot(true, 1, "Split detector process and edit tracking"));

            Assert.NotEqual(CodexActivityKind.Refactoring, snapshot.ActivityKind);
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    [Fact]
    public void Test_15_StaleRecentEdit_FallsBackToAnalyzingProject()
    {
        var tempPath = CreateTempSessionDirectory();
        try
        {
            var now = DateTime.UtcNow;
            WriteMockSessionLog(tempPath, "session1.jsonl", new[]
            {
                $"{{\"timestamp\":\"{now:yyyy-MM-ddTHH:mm:ss.fffZ}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\",\"turn_id\":\"123\"}}}}"
            });

            var projectRoot = Path.Combine(Path.GetTempPath(), "CodexStaleEditProject_" + Guid.NewGuid());
            Directory.CreateDirectory(projectRoot);

            try
            {
                var file = Path.Combine(projectRoot, "Stale.cs");
                File.WriteAllText(file, "1");
                File.SetLastWriteTimeUtc(file, now.AddSeconds(-30));

                var projectSnapshot = new ProjectSnapshot(
                    Path.GetFileName(projectRoot),
                    projectRoot,
                    "Stale.cs",
                    file,
                    1,
                    1,
                    2,
                    [new RecentProjectFileSnapshot("Stale.cs", file, File.GetLastWriteTimeUtc(file))]);

                var detector = new CodexProcessDetector(
                    new CodexDetectionOptions { HomePath = tempPath },
                    new PresenceTemplateOptions { EditingFreshnessSeconds = 12 });

                var snapshot = detector.GetSnapshot(projectRoot, projectSnapshot, new GitSnapshot(true, 1, null));

                Assert.True(snapshot.IsRunning);
                Assert.Equal(CodexActivityKind.AnalyzingProject, snapshot.ActivityKind);
            }
            finally
            {
                Directory.Delete(projectRoot, true);
            }
        }
        finally
        {
            Directory.Delete(tempPath, true);
        }
    }

    private static string EscapeJson(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}

