using System.Text.Json;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexSessionLogParserTests
{
    [Fact]
    public void InspectRecentSessions_EmitsTurnAndToolLifecycleEvents()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexEventProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "call-1",
                    name = "apply_patch",
                    input = $"*** Begin Patch\\n*** Update File: {Path.Combine(projectPath, "First.cs")}\\n*** Update File: {Path.Combine(projectPath, "Second.cs")}\\n*** End Patch"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(2), new
                {
                    type = "custom_tool_call_output",
                    turn_id = "turn-1",
                    call_id = "call-1"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(3), new
                {
                    type = "task_complete",
                    turn_id = "turn-1"
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(
                [
                    CodexActivityEventKind.TurnStarted,
                    CodexActivityEventKind.OperationStarted,
                    CodexActivityEventKind.OperationCompleted,
                    CodexActivityEventKind.TurnCompleted
                ],
                inspection!.ActivityEvents.Select(activityEvent => activityEvent.Kind).ToArray());

            var operation = inspection.ActivityEvents[1];
            Assert.Equal(CodexOperationKind.Edit, operation.OperationKind);
            Assert.Equal(2, operation.TargetPaths.Count);
            Assert.Equal("turn-1", operation.TurnId);
            Assert.Equal("call-1", operation.CallId);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_ClassifiesAddAndDeletePatchesWithoutLeakingPatchBody()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPatchKindProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var createdFile = Path.Combine(projectPath, "Created.cs");
            var deletedFile = Path.Combine(projectPath, "Deleted.cs");
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "create-call",
                    name = "apply_patch",
                    input = $"*** Begin Patch\\n*** Add File: {createdFile}\\n+probe-created\\n*** End Patch"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(2), new
                {
                    type = "custom_tool_call_output",
                    turn_id = "turn-1",
                    call_id = "create-call"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(3), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "delete-call",
                    name = "apply_patch",
                    input = $"*** Begin Patch\\n*** Delete File: {deletedFile}\\n*** End Patch"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var operations = inspection!.ActivityEvents
                .Where(activityEvent => activityEvent.Kind == CodexActivityEventKind.OperationStarted)
                .ToArray();
            Assert.Equal(2, operations.Length);

            Assert.Equal(CodexOperationKind.Create, operations[0].OperationKind);
            Assert.Equal(Path.GetFullPath(createdFile), Assert.Single(operations[0].TargetPaths));
            Assert.Equal(CodexOperationKind.Delete, operations[1].OperationKind);
            Assert.Equal(Path.GetFullPath(deletedFile), Assert.Single(operations[1].TargetPaths));
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_ClassifiesShellCommandSeparatelyFromMutation()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexReadEventProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "function_call",
                    turn_id = "turn-1",
                    call_id = "call-1",
                    name = "shell_command",
                    arguments = JsonSerializer.Serialize(new { command = "Get-Content README.md" })
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var operation = Assert.Single(inspection!.ActivityEvents.Skip(1));
            Assert.Equal(CodexActivityEventKind.OperationStarted, operation.Kind);
            Assert.Equal(CodexOperationKind.Command, operation.OperationKind);
            Assert.Equal(RunningCommandKind.Search, operation.CommandKind);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_ClassifiesNestedCommandToolCallAsCommand()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexNestedCommandProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "call-1",
                    name = "exec",
                    input = "text(await tools.exec_command({ cmd: \"dotnet test\" }));"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var operation = Assert.Single(inspection!.ActivityEvents.Skip(1));
            Assert.Equal(CodexActivityEventKind.OperationStarted, operation.Kind);
            Assert.Equal(CodexOperationKind.Command, operation.OperationKind);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

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
    public void InspectRecentSessions_PrefersSessionWithPendingMutationOverNewerIdleSession()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPendingSessionProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var activeFile = Path.Combine(projectPath, "Active.cs");
            WriteSession(homePath, "idle-session.jsonl",
            [
                CreateSessionLine(now.AddSeconds(2), new
                {
                    type = "task_started",
                    turn_id = "idle-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddSeconds(3), new
                {
                    type = "reasoning",
                    turn_id = "idle-turn"
                }, "event_msg")
            ]);
            WriteSession(homePath, "pending-session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "pending-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "pending-turn",
                    call_id = "pending-call",
                    name = "apply_patch",
                    input = $"*** Begin Patch\\n*** Update File: {activeFile}\\n*** End Patch"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(Path.GetFullPath(activeFile), inspection!.LastDirectToolFilePath);
            Assert.Contains(inspection.ActivityEvents, activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.OperationStarted &&
                activityEvent.OperationKind == CodexOperationKind.Edit);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_IgnoresInterpolatedPatchPlaceholder()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPlaceholderProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "call-1",
                    name = "exec",
                    input = "const patch = \"*** Update File: {filePath}\";"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var operation = Assert.Single(inspection!.ActivityEvents.Skip(1));
            Assert.Equal(CodexOperationKind.Unknown, operation.OperationKind);
            Assert.Empty(operation.TargetPaths);
            Assert.Null(inspection.LastDirectToolFilePath);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_IgnoresPatchSyntaxEmbeddedInToolSource()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPatchSourceProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "session.jsonl",
            [
                CreateSessionLine(now, new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(1), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "call-1",
                    name = "exec",
                    input = "var source = \"*** Add File: Example.cs\\n+source-only\";"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var operation = Assert.Single(inspection!.ActivityEvents.Skip(1));
            Assert.Equal(CodexActivityEventKind.OperationStarted, operation.Kind);
            Assert.Equal(CodexOperationKind.Unknown, operation.OperationKind);
            Assert.Empty(operation.TargetPaths);
            Assert.Null(inspection.LastDirectToolFilePath);
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

    [Fact]
    public void InspectRecentSessions_LargeHistoryStillReadsCurrentTail()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexLargeSessionProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var activeFile = Path.Combine(projectPath, "Current.cs");
            var lines = new List<string>
            {
                CreateSessionLine(now.AddMinutes(-1), new
                {
                    type = "task_started",
                    turn_id = "turn-1",
                    cwd = projectPath
                }, "event_msg")
            };
            var filler = CreateSessionLine(now.AddSeconds(-30), new { type = "token_count" }, "event_msg");
            lines.AddRange(Enumerable.Repeat(filler, 30000));
            lines.Add(CreateSessionLine(now, new
            {
                type = "custom_tool_call",
                turn_id = "turn-1",
                call_id = "call-1",
                name = "apply_patch",
                input = $"*** Begin Patch\n*** Update File: {activeFile}\n*** End Patch"
            }, "response_item"));
            WriteSession(homePath, "large-session.jsonl", lines);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(Path.GetFullPath(activeFile), inspection!.LastDirectToolFilePath);
            Assert.Contains(inspection.ActivityEvents, activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.OperationStarted &&
                activityEvent.TargetPaths.Contains(Path.GetFullPath(activeFile), StringComparer.OrdinalIgnoreCase));
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
