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
}
