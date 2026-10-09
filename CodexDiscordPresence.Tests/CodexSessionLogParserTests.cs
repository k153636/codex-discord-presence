using System.Text.Json;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexSessionLogParserTests
{
    [Fact]
    public void InspectRecentSessions_ExtractsLatestReasoningSummaryFromResponseItem()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexReasoningSummaryProject_" + Guid.NewGuid());
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
                    type = "reasoning",
                    turn_id = "turn-1",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Inspecting the activity state**" },
                        new { type = "summary_text", text = "**Designing the presence summary format**" }
                    },
                    encrypted_content = "not used"
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            var reasoning = Assert.Single(inspection!.ActivityEvents, activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.Reasoning);
            Assert.Equal("Designing the presence summary format", reasoning.ThinkingSummary);
            Assert.Equal("Designing the presence summary format", inspection.LatestThinkingSummary);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_ExtractsReasoningSummaryFromCompletedItem()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexCompletedReasoningProject_" + Guid.NewGuid());
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
                    type = "item_completed",
                    item = new
                    {
                        type = "Reasoning",
                        summary_text = new[]
                        {
                            new { type = "summary_text", text = "**Confirming the final state**" }
                        }
                    }
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Contains(inspection!.ActivityEvents, activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.Reasoning &&
                activityEvent.ThinkingSummary == "Confirming the final state");
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_TracksActiveSubagentsFromCollabAgentToolCalls()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPartyProject_" + Guid.NewGuid());
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
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "exec-spawn-1",
                        tool = "spawn_agent",
                        receiver_agents = new[]
                        {
                            new { thread_id = "agent-1" },
                            new { thread_id = "agent-2" }
                        }
                    }
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(2), new
                {
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "exec-spawn-2",
                        tool = "spawn_agent",
                        receiver_agents = new[]
                        {
                            new { thread_id = "agent-1" }
                        }
                    }
                }, "event_msg"),
                CreateSessionLine(now.AddMilliseconds(3), new
                {
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "exec-close-1",
                        tool = "close_agent",
                        receiver_agents = new[]
                        {
                            new { thread_id = "agent-1" }
                        }
                    }
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(2, inspection!.ActivityEvents.Count(activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.AgentStarted));
            Assert.Single(inspection.ActivityEvents, activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.AgentCompleted);
            Assert.Equal(["agent-2"], inspection.ActiveAgentThreadIds);
            Assert.Equal(2, inspection.PartySize);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_IgnoresAgentEventsWithoutReceiverThreadId()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPartyMissingIdProject_" + Guid.NewGuid());
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
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "exec-without-agent-id",
                        tool = "spawn_agent"
                    }
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.DoesNotContain(inspection!.ActivityEvents, activityEvent =>
                activityEvent.Kind is CodexActivityEventKind.AgentStarted or CodexActivityEventKind.AgentCompleted);
            Assert.Equal(1, inspection.PartySize);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_SelectsPrimaryThreadWhenSubagentIsNewer()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexPrimaryThreadProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var mainThreadId = "main-thread";
            var mainSessionPath = Path.Combine(homePath, "sessions", "main.jsonl");
            var subagentSessionPath = Path.Combine(homePath, "sessions", "subagent.jsonl");

            WriteSession(homePath, "main.jsonl",
            [
                CreateSessionMetaLine(now.AddMinutes(-1), mainThreadId, "user", null, projectPath),
                CreateSessionLine(now.AddSeconds(-30), new
                {
                    type = "task_started",
                    turn_id = "main-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddSeconds(-29), new
                {
                    type = "reasoning",
                    turn_id = "main-turn",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Main thread summary**" }
                    }
                }, "response_item"),
                CreateSessionLine(now.AddSeconds(-28), new
                {
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "spawn-main-agent",
                        tool = "spawn_agent",
                        receiver_agents = new[]
                        {
                            new { thread_id = "agent-1" }
                        }
                    }
                }, "event_msg")
            ]);

            WriteSession(homePath, "subagent.jsonl",
            [
                CreateNestedSubagentSessionMetaLine(now, "agent-1", mainThreadId, projectPath),
                CreateSessionLine(now.AddSeconds(1), new
                {
                    type = "task_started",
                    turn_id = "subagent-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(now.AddSeconds(2), new
                {
                    type = "reasoning",
                    turn_id = "subagent-turn",
                    summary = new[]
                    {
                        new { type = "summary_text", text = "**Subagent summary must not be displayed**" }
                    }
                }, "response_item")
            ]);

            File.SetLastWriteTimeUtc(mainSessionPath, now.AddSeconds(-10));
            File.SetLastWriteTimeUtc(subagentSessionPath, now);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.True(inspection!.IsPrimaryThread);
            Assert.Equal(mainThreadId, inspection.ThreadId);
            Assert.Equal("user", inspection.ThreadSource);
            Assert.Null(inspection.ParentThreadId);
            Assert.Equal("Main thread summary", inspection.LatestThinkingSummary);
            Assert.Equal(2, inspection.PartySize);
            Assert.Equal(1, inspection.SubagentActivity?.ActiveCount);
            Assert.Equal(1, inspection.SubagentActivity?.ThinkingCount);
            Assert.Equal(0, inspection.SubagentActivity?.UnknownCount);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_ActiveSubagentWithStaleChildEventsUsesGenericStatus()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexStaleChildProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var old = now.AddMinutes(-2);
            WriteSession(homePath, "main.jsonl",
            [
                CreateSessionMetaLine(old, "main-thread", "user", null, projectPath),
                CreateSessionLine(old.AddSeconds(1), new
                {
                    type = "task_started",
                    turn_id = "main-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(old.AddSeconds(2), new
                {
                    type = "item_completed",
                    item = new
                    {
                        type = "CollabAgentToolCall",
                        id = "spawn-main-agent",
                        tool = "spawn_agent",
                        receiver_agents = new[] { new { thread_id = "agent-1" } }
                    }
                }, "event_msg")
            ]);
            WriteSession(homePath, "subagent.jsonl",
            [
                CreateNestedSubagentSessionMetaLine(old, "agent-1", "main-thread", projectPath),
                CreateSessionLine(old.AddSeconds(3), new
                {
                    type = "task_started",
                    turn_id = "child-turn",
                    cwd = projectPath
                }, "event_msg"),
                CreateSessionLine(old.AddSeconds(4), new
                {
                    type = "reasoning",
                    turn_id = "child-turn",
                    summary = new[] { new { type = "summary_text", text = "private child summary" } }
                }, "response_item")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal(2, inspection!.PartySize);
            Assert.Equal(1, inspection.SubagentActivity?.ActiveCount);
            Assert.Equal(0, inspection.SubagentActivity?.KnownCount);
            Assert.Equal(1, inspection.SubagentActivity?.UnknownCount);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_DoesNotTreatSubagentThreadAsMainWhenItIsTheOnlyCandidate()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexSubagentOnlyProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            WriteSession(homePath, "subagent-only.jsonl",
            [
                CreateSessionMetaLine(now, "subagent-thread", "subagent", "missing-main-thread", projectPath),
                CreateSessionLine(now.AddSeconds(1), new
                {
                    type = "task_started",
                    turn_id = "subagent-turn",
                    cwd = projectPath
                }, "event_msg")
            ]);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            Assert.Null(parser.InspectRecentSessions(projectPath));
            Assert.Null(parser.GetLatestObservedProjectPath());
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

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
    public void InspectRecentSessions_ClassifiesNestedExecApplyPatchLifecycle()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexNestedPatchLifecycleProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var filePath = Path.Combine(projectPath, "Probe.txt");
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
                    name = "exec",
                    input = $"const patch = \"*** Begin Patch\\n*** Add File: {filePath}\\n+created\\n*** End Patch\"; text(await tools.apply_patch(patch));"
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
                    call_id = "edit-call",
                    name = "exec",
                    input = $"const patch = \"*** Begin Patch\\n*** Update File: {filePath}\\n@@\\n-created\\n+edited\\n*** End Patch\"; text(await tools.apply_patch(patch));"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(4), new
                {
                    type = "custom_tool_call_output",
                    turn_id = "turn-1",
                    call_id = "edit-call"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(5), new
                {
                    type = "custom_tool_call",
                    turn_id = "turn-1",
                    call_id = "delete-call",
                    name = "exec",
                    input = $"const patch = \"*** Begin Patch\\n*** Delete File: {filePath}\\n*** End Patch\"; text(await tools.apply_patch(patch));"
                }, "response_item"),
                CreateSessionLine(now.AddMilliseconds(6), new
                {
                    type = "custom_tool_call_output",
                    turn_id = "turn-1",
                    call_id = "delete-call"
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

            Assert.Equal(
                [CodexOperationKind.Create, CodexOperationKind.Edit, CodexOperationKind.Delete],
                operations.Select(operation => operation.OperationKind).ToArray());
            Assert.All(operations, operation =>
                Assert.Equal(Path.GetFullPath(filePath), Assert.Single(operation.TargetPaths)));
            Assert.Equal(3, inspection.ActivityEvents.Count(activityEvent =>
                activityEvent.Kind == CodexActivityEventKind.OperationCompleted));
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
            Assert.Contains(inspection.ActivityEvents, activityEvent => activityEvent.IsMcpOperation);
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

    [Fact]
    public void InspectRecentSessions_LargeHistoryKeepsTaskBoundaryOutsideTail()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexLargeCompletedSessionProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var filler = CreateSessionLine(now, new { type = "token_count" }, "event_msg");
            var lines = new List<string>
            {
                CreateSessionLine(now.AddMinutes(-2), new
                {
                    type = "task_started",
                    turn_id = "turn-completed",
                    cwd = projectPath
                }, "event_msg")
            };
            lines.AddRange(Enumerable.Repeat(filler, 30000));
            lines.Add(CreateSessionLine(now.AddMinutes(-1), new
            {
                type = "task_complete",
                turn_id = "turn-completed"
            }, "event_msg"));
            lines.AddRange(Enumerable.Repeat(filler, 30000));
            WriteSession(homePath, "large-completed-session.jsonl", lines);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.True(inspection!.HasTaskCompletedSinceStart);
            Assert.Equal("turn-completed", inspection.GetActivityStateAt(DateTime.UtcNow)?.TurnId);
            Assert.Equal(CodexTurnLifecycle.Completed, inspection.GetActivityStateAt(DateTime.UtcNow)?.Lifecycle);
        }
        finally
        {
            Directory.Delete(homePath, true);
            Directory.Delete(projectPath, true);
        }
    }

    [Fact]
    public void InspectRecentSessions_LargeHistoryReadsLatestSessionSettingsOutsideTail()
    {
        var homePath = CreateTempCodexHome();
        var projectPath = Path.Combine(Path.GetTempPath(), "CodexLargeSessionSettingsProject_" + Guid.NewGuid());
        Directory.CreateDirectory(projectPath);

        try
        {
            var now = DateTime.UtcNow;
            var lines = new List<string>
            {
                CreateSessionLine(now.AddMinutes(-2), new
                {
                    session_id = "settings-session",
                    id = "settings-session",
                    thread_source = "user",
                    cwd = projectPath,
                    model = "gpt-5.6-luna",
                    reasoning_effort = "xhigh",
                    service_tier = "priority"
                }, "session_meta")
            };
            var filler = CreateSessionLine(now.AddMinutes(-1), new { type = "token_count" }, "event_msg");
            lines.AddRange(Enumerable.Repeat(filler, 300));
            lines.Add(CreateSessionLine(now, new
            {
                type = "thread_settings_applied",
                thread_settings = new
                {
                    model = "gpt-5.6-luna",
                    reasoning_effort = "max",
                    service_tier = "default"
                }
            }, "event_msg"));
            lines.Add(CreateSessionLine(now.AddSeconds(1), new
            {
                type = "token_count",
                padding = new string('x', 2_100_000)
            }, "event_msg"));
            WriteSession(homePath, "large-settings-session.jsonl", lines);

            var parser = new CodexSessionLogParser(
                new CodexDetectionOptions { HomePath = homePath },
                new PresenceTemplateOptions());

            var inspection = parser.InspectRecentSessions(projectPath);

            Assert.NotNull(inspection);
            Assert.Equal("gpt-5.6-luna", inspection!.ModelName);
            Assert.Equal("max", inspection.ReasoningEffort);
            Assert.Equal("default", inspection.ServiceTier);
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

    private static string CreateSessionMetaLine(
        DateTime timestamp,
        string threadId,
        string threadSource,
        string? parentThreadId,
        string projectPath)
    {
        return CreateSessionLine(timestamp, new
        {
            session_id = threadId,
            id = threadId,
            parent_thread_id = parentThreadId,
            thread_source = threadSource,
            cwd = projectPath,
            source = "cli"
        }, "session_meta");
    }

    private static string CreateNestedSubagentSessionMetaLine(
        DateTime timestamp,
        string threadId,
        string parentThreadId,
        string projectPath)
    {
        return CreateSessionLine(timestamp, new
        {
            session_id = threadId,
            id = threadId,
            source = new
            {
                subagent = new
                {
                    thread_spawn = new
                    {
                        parent_thread_id = parentThreadId,
                        agent_id = "redacted-agent-id"
                    }
                }
            },
            cwd = projectPath
        }, "session_meta");
    }
}
