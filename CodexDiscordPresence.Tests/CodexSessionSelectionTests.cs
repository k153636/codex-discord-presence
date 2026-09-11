using System.Text.Json;

namespace CodexDiscordPresence.Tests;

public sealed class CodexSessionSelectionTests
{
    [Fact]
    public void InspectRecentSessions_DoesNotPreferStalledPendingMutationOverFreshSession()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexSessionSelectionProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var stale = now.AddMinutes(-5);
            var targetPath = Path.Combine(projectPath, "Stale.cs");

            WriteSession(homePath, "stale-pending.jsonl",
            [
                CreateSessionLine(stale, new
                {
                    type = "task_started",
                    turn_id = "stale-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(stale.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "stale-turn",
                    call_id = "stale-call",
                    name = "apply_patch",
                    input = $"*** Begin Patch\n*** Update File: {targetPath}\n*** End Patch"
                }, "response_item")
            ]);
            WriteSession(homePath, "fresh-session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "fresh-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "reasoning",
                    turn_id = "fresh-turn",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Fresh session**" }
                    }
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 1 });

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal("Fresh session", inspection!.LatestThinkingSummary);
            Assert.DoesNotContain(inspection.ActivityEvents, activityEvent =>
                activityEvent.OperationKind == CodexOperationKind.Edit &&
                activityEvent.TargetPaths.Contains(Path.GetFullPath(targetPath), StringComparer.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_PrefersActivePrimaryOverNewerIdlePrimary()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexSessionSelectionActiveProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "active-primary.jsonl",
            [
                CreateSessionMetaLine(
                    now.AddMinutes(-2),
                    "active-primary",
                    projectPath,
                    "gpt-active",
                    "xhigh",
                    "default"),
                CreateSessionLine(now.AddSeconds(-2), new
                {
                    type = "task_started",
                    turn_id = "active-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddSeconds(-1), new
                {
                    type = "reasoning",
                    turn_id = "active-turn",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Active primary**" }
                    }
                }, "response_item")
            ]);
            WriteSession(homePath, "idle-primary.jsonl",
            [
                CreateSessionMetaLine(
                    now,
                    "idle-primary",
                    projectPath,
                    "gpt-idle",
                    "max",
                    "priority")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 1 });

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal("active-primary", inspection!.ThreadId);
            Assert.Equal("gpt-active", inspection.ModelName);
            Assert.Equal("xhigh", inspection.ReasoningEffort);
            Assert.Equal("default", inspection.ServiceTier);
            Assert.Equal("Active primary", inspection.LatestThinkingSummary);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_KeepsPreviouslySelectedPrimaryWhenBothAreIdle()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexSessionSelectionStickyProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var firstSessionPath = Path.Combine(homePath, "sessions", "first-primary.jsonl");
            WriteSession(homePath, "first-primary.jsonl",
            [
                CreateSessionMetaLine(
                    now.AddMinutes(-2),
                    "first-primary",
                    projectPath,
                    "gpt-first",
                    "xhigh",
                    "default")
            ]);
            File.SetLastWriteTimeUtc(firstSessionPath, now.AddSeconds(-10));

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions { ThinkingStaleTimeoutMinutes = 1 });

            var firstInspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(firstInspection);
            Assert.Equal("first-primary", firstInspection!.ThreadId);

            var secondSessionPath = Path.Combine(homePath, "sessions", "newer-idle-primary.jsonl");
            WriteSession(homePath, "newer-idle-primary.jsonl",
            [
                CreateSessionMetaLine(
                    now,
                    "newer-idle-primary",
                    projectPath,
                    "gpt-second",
                    "max",
                    "priority")
            ]);
            File.SetLastWriteTimeUtc(secondSessionPath, now.AddSeconds(10));

            var secondInspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(secondInspection);
            Assert.Equal("first-primary", secondInspection!.ThreadId);
            Assert.Equal("gpt-first", secondInspection.ModelName);
            Assert.Equal("xhigh", secondInspection.ReasoningEffort);
            Assert.Equal("default", secondInspection.ServiceTier);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_FindsProjectSessionOutsideGlobalRecentFileLimit()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexSessionProjectLimitProject_" + Guid.NewGuid());
        var otherProjectPath = Path.Combine(Path.GetTempPath(), "CodexSessionProjectLimitOther_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);
        Directory.CreateDirectory(otherProjectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "project-session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "project-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "reasoning",
                    turn_id = "project-turn",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Project session**" }
                    }
                }, "response_item")
            ]);
            var projectSessionPath = Path.Combine(homePath, "sessions", "project-session.jsonl");
            File.SetLastWriteTimeUtc(projectSessionPath, now.AddMinutes(-1));

            WriteSession(homePath, "unrelated-session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "other-turn",
                    cwd = otherProjectPath
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions
                {
                    HomePath = homePath,
                    RecentSessionFilesToScan = 1
                },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.True(inspection!.MatchesProject);
            Assert.Equal("Project session", inspection.LatestThinkingSummary);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
            Directory.Delete(otherProjectPath, true);
        }
    }

    private static string CreateTempCodexHome()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexSessionSelectionTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(path, "sessions"));
        return path;
    }

    private static void WriteSession(string homePath, string fileName, IEnumerable<string> lines)
    {
        File.WriteAllLines(Path.Combine(homePath, "sessions", fileName), lines);
    }

    private static string CreateSessionLine(DateTime timestamp, object payload, string type)
    {
        return JsonSerializer.Serialize(new
        {
            timestamp = timestamp.ToString("O"),
            type,
            payload
        });
    }

    private static string CreateSessionMetaLine(
        DateTime timestamp,
        string threadId,
        string projectPath,
        string model,
        string reasoningEffort,
        string serviceTier)
    {
        return CreateSessionLine(timestamp, new
        {
            session_id = threadId,
            id = threadId,
            thread_source = "user",
            cwd = projectPath,
            model,
            reasoning_effort = reasoningEffort,
            service_tier = serviceTier
        }, "session_meta");
    }
}
