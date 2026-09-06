using System.Text.Json;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexEventDrivenPresenceTests
{
    [Fact]
    public void GetSnapshot_SinglePendingEdit_UsesDirectToolTarget()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var filePath = Path.Combine(projectPath, "src", "PresenceRuntime.cs");
        var homePath = CreateHomePath();

        try
        {
            WriteSession(homePath, "session.jsonl", CreateLifecycle(
                now,
                projectPath,
                new ToolCall("call-1", filePath, now.AddMilliseconds(1))));

            var snapshot = CreateDetector(homePath).GetSnapshot(projectPath);

            Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
            Assert.Equal(filePath, snapshot.ActiveToolFilePath);
            Assert.Equal([filePath], snapshot.ActivityFilePaths);
            Assert.Equal(1, snapshot.PendingMutationCount);
        }
        finally
        {
            DeleteDirectory(homePath);
        }
    }

    [Fact]
    public void Render_TwoOrThreeSequentialEdits_UsesCurrentToolFileWithoutHistoryList()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var firstFile = Path.Combine(projectPath, "First.cs");
        var activeFile = Path.Combine(projectPath, "src", "Active.cs");
        var homePath = CreateHomePath();

        try
        {
            WriteSession(homePath, "session.jsonl", CreateLifecycle(
                now,
                projectPath,
                new ToolCall("call-1", firstFile, now.AddMilliseconds(1), Completed: true),
                new ToolCall("call-2", activeFile, now.AddMilliseconds(3))));

            var snapshot = CreateDetector(homePath).GetSnapshot(projectPath);
            var presence = Render(projectPath, snapshot);

            Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
            Assert.Equal(activeFile, snapshot.ActiveToolFilePath);
            Assert.Equal(2, snapshot.ActivityFilePaths.Count);
            Assert.Equal("Editing Active.cs", presence.State);
        }
        finally
        {
            DeleteDirectory(homePath);
        }
    }

    [Fact]
    public void Render_FourSequentialEdits_UsesCurrentToolFileAndRemainingCount()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var files = Enumerable.Range(1, 4)
            .Select(index => Path.Combine(projectPath, $"File{index}.cs"))
            .ToArray();
        var homePath = CreateHomePath();

        try
        {
            var toolCalls = files
                .Select((filePath, index) => new ToolCall(
                    $"call-{index + 1}",
                    filePath,
                    now.AddMilliseconds(index + 1),
                    Completed: index < files.Length - 1))
                .ToArray();
            WriteSession(homePath, "session.jsonl", CreateLifecycle(now, projectPath, toolCalls));

            var snapshot = CreateDetector(homePath).GetSnapshot(projectPath);
            var presence = Render(projectPath, snapshot);

            Assert.Equal(files[^1], snapshot.ActiveToolFilePath);
            Assert.Equal(4, snapshot.ActivityFilePaths.Count);
            Assert.Equal("Editing File4.cs + 3 files", presence.State);
        }
        finally
        {
            DeleteDirectory(homePath);
        }
    }

    [Fact]
    public void Render_MultiTargetToolCall_UsesLatestDirectFileAndRemainingCount()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var files = Enumerable.Range(1, 18)
            .Select(index => Path.Combine(projectPath, $"File{index}.cs"))
            .ToArray();
        var homePath = CreateHomePath();

        try
        {
            WriteSession(homePath, "session.jsonl", CreateLifecycle(
                now,
                projectPath,
                new ToolCall("call-1", files[0], now.AddMilliseconds(1),
                    Completed: false,
                    AdditionalPaths: files.Skip(1).ToArray())));

            var snapshot = CreateDetector(homePath).GetSnapshot(projectPath);
            var presence = Render(projectPath, snapshot);

            Assert.Equal(CodexActivityKind.ApplyingEdits, snapshot.ActivityKind);
            Assert.Null(snapshot.ActiveToolFilePath);
            Assert.Equal(18, snapshot.ActivityFilePaths.Count);
            Assert.Equal("Editing File18.cs + 17 files", presence.State);
            Assert.DoesNotContain("File1.cs", presence.State, StringComparison.Ordinal);
        }
        finally
        {
            DeleteDirectory(homePath);
        }
    }

    [Fact]
    public void GetSnapshot_CompletedTurn_ClearsPendingEditAndActiveFile()
    {
        var now = DateTime.UtcNow;
        var projectPath = CreateProjectPath();
        var filePath = Path.Combine(projectPath, "Completed.cs");
        var homePath = CreateHomePath();

        try
        {
            WriteSession(homePath, "session.jsonl", CreateLifecycle(
                now,
                projectPath,
                new[] { new ToolCall("call-1", filePath, now.AddMilliseconds(1), Completed: true) },
                CompletedTurn: true));

            var snapshot = CreateDetector(homePath).GetSnapshot(projectPath);

            Assert.Equal(CodexActivityKind.Ready, snapshot.ActivityKind);
            Assert.Null(snapshot.ActiveToolFilePath);
            Assert.Empty(snapshot.ActivityFilePaths);
            Assert.Equal(0, snapshot.PendingOperationCount);
        }
        finally
        {
            DeleteDirectory(homePath);
        }
    }

    private static CodexProcessDetector CreateDetector(string homePath)
    {
        return new CodexProcessDetector(
            new CodexDetectionOptions
            {
                HomePath = homePath,
                ProcessNameContains = ["__codex_event_test_process__"],
                WindowTitleContains = ["__codex_event_test_window__"]
            },
            new PresenceTemplateOptions());
    }

    private static RenderedPresence Render(string projectPath, CodexProcessSnapshot snapshot)
    {
        var now = DateTime.UtcNow;
        return new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            new PresenceContext(
                "Codex",
                snapshot,
                new ProjectSnapshot(
                    "EventDrivenProject",
                    projectPath,
                    null,
                    null,
                    snapshot.ActivityFilePaths.Count,
                    snapshot.ActivityFilePaths.Count,
                    0,
                    []),
                new GitSnapshot(true, snapshot.ActivityFilePaths.Count, null),
                new SessionSnapshot(now, TimeSpan.Zero),
                new TokenUsageSnapshot(null, null)));
    }

    private static IReadOnlyList<string> CreateLifecycle(
        DateTime startedAt,
        string projectPath,
        params ToolCall[] toolCalls)
    {
        return CreateLifecycle(startedAt, projectPath, toolCalls, CompletedTurn: false);
    }

    private static IReadOnlyList<string> CreateLifecycle(
        DateTime startedAt,
        string projectPath,
        IReadOnlyList<ToolCall> toolCalls,
        bool CompletedTurn)
    {
        var lines = new List<string>
        {
            CreateSessionLine(startedAt, new
            {
                type = "task_started",
                turn_id = "turn-1",
                cwd = projectPath
            }, "event_msg")
        };

        foreach (var toolCall in toolCalls)
        {
            var patch = string.Join(
                "\n",
                [
                    "*** Begin Patch",
                    $"*** Update File: {toolCall.FilePath}",
                    ..toolCall.AdditionalTargetPaths.Select(path => $"*** Update File: {path}"),
                    "*** End Patch"
                ]);
            lines.Add(CreateSessionLine(toolCall.TimestampUtc, new
            {
                type = "custom_tool_call",
                turn_id = "turn-1",
                call_id = toolCall.CallId,
                name = "apply_patch",
                input = patch
            }, "response_item"));

            if (toolCall.Completed)
            {
                lines.Add(CreateSessionLine(
                    toolCall.TimestampUtc.AddMilliseconds(1),
                    new
                    {
                        type = "custom_tool_call_output",
                        turn_id = "turn-1",
                        call_id = toolCall.CallId
                    },
                    "response_item"));
            }
        }

        if (CompletedTurn)
        {
            lines.Add(CreateSessionLine(
                startedAt.AddSeconds(1),
                new
                {
                    type = "task_complete",
                    turn_id = "turn-1"
                },
                "event_msg"));
        }

        return lines;
    }

    private static string CreateHomePath()
    {
        var path = Path.Combine(Path.GetTempPath(), "CodexEventDrivenTests_" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(path, "sessions"));
        return path;
    }

    private static string CreateProjectPath()
    {
        return Path.Combine(Path.GetTempPath(), "CodexEventDrivenProject_" + Guid.NewGuid());
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

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private sealed record ToolCall(
        string CallId,
        string FilePath,
        DateTime TimestampUtc,
        bool Completed = false,
        IReadOnlyList<string>? AdditionalPaths = null)
    {
        public IReadOnlyList<string> AdditionalTargetPaths { get; } = AdditionalPaths ?? [];
    }
}
