using System.Text.Json;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexSessionLogParserTests
{
    [Fact]
    public void InspectRecentSessions_UsesLastFileFromApplyPatchAsDirectTarget()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexDirectTargetProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var firstFile = Path.Combine(projectPath, "First.cs");
            var activeFile = Path.Combine(projectPath, "src", "new", "Active.cs");
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    name = "exec",
                    input = $"const patch = \"*** Begin Patch\\n*** Update File: {firstFile}\\n@@\\n*** Update File: {activeFile}\\n@@\\n*** End Patch\"; text(await tools.apply_patch(patch));"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(Path.GetFullPath(activeFile), inspection!.LastDirectToolFilePath);
            Assert.Equal(now.AddMilliseconds(1), inspection.LastDirectToolFileAt);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_UsesFileTargetFromMcpMutationTool()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexMcpTargetProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var activeFile = Path.Combine(projectPath, "Presence.cs");
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "mcp_tool_call_end",
                    invocation = new
                    {
                        tool = "apply_patch",
                        arguments = new
                        {
                            target_file = activeFile
                        }
                    }
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(Path.GetFullPath(activeFile), inspection!.LastDirectToolFilePath);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_IgnoresReadOnlyToolInputAsDirectTarget()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexReadOnlyTargetProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    name = "exec",
                    input = $"text(await tools.exec_command({{ cmd: \"Get-Content '{Path.Combine(projectPath, "README.md")}\" }}));"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Null(inspection!.LastDirectToolFilePath);
            Assert.Null(inspection.LastDirectToolFileAt);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    private static string CreateTempCodexHome()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexSessionParserTests_" + Guid.NewGuid());
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
