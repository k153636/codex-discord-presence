namespace CodexDiscordPresence.Tests;

public sealed class CodexActivityStateMachineTests
{
    [Fact]
    public void Evaluate_KeepsLatestThinkingSummaryForCurrentTurnAndClearsItOnNewTurn()
    {
        var startedAt = Utc(5);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(2, startedAt.AddSeconds(1), CodexActivityEventKind.Reasoning, turnId: "turn-1", thinkingSummary: "First summary"),
            Event(3, startedAt.AddSeconds(2), CodexActivityEventKind.Reasoning, turnId: "turn-1", thinkingSummary: "Latest summary"),
            Event(4, startedAt.AddSeconds(3), CodexActivityEventKind.TurnCompleted, turnId: "turn-1"),
            Event(5, startedAt.AddSeconds(4), CodexActivityEventKind.TurnStarted, turnId: "turn-2"));

        Assert.Equal("turn-2", state.TurnId);
        Assert.Null(state.LatestThinkingSummary);

        var currentTurnState = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(2, startedAt.AddSeconds(1), CodexActivityEventKind.Reasoning, turnId: "turn-1", thinkingSummary: "First summary"),
            Event(3, startedAt.AddSeconds(2), CodexActivityEventKind.Reasoning, turnId: "turn-1", thinkingSummary: "Latest summary"));

        Assert.Equal("Latest summary", currentTurnState.LatestThinkingSummary);
    }

    [Fact]
    public void Evaluate_PendingEditUsesDirectTargetAsActiveFile()
    {
        var startedAt = Utc(10);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\PresenceRuntime.cs"]));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(CodexOperationKind.Edit, state.OperationKind);
        Assert.Equal(@"E:\repo\PresenceRuntime.cs", state.ActiveFilePath);
        Assert.Single(state.MutationFilePaths);
        Assert.Equal(1, state.PendingMutationCount);
    }

    [Fact]
    public void Evaluate_CompletedEditIsNotStillPending()
    {
        var startedAt = Utc(20);
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\PresenceRuntime.cs"]),
            Event(
                3,
                startedAt.AddMilliseconds(200),
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "call-1"),
            startedAt.AddSeconds(9));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(CodexOperationKind.Unknown, state.OperationKind);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Equal(0, state.PendingMutationCount);
        Assert.Null(state.ActiveFilePath);
        Assert.Single(state.MutationFilePaths);
    }

    [Fact]
    public void Evaluate_CompletedEditRemainsVisibleOnlyDuringShortPropagationGrace()
    {
        var startedAt = Utc(24);
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\PresenceRuntime.cs"]),
            Event(
                3,
                startedAt.AddMilliseconds(200),
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "call-1"),
            startedAt.AddSeconds(1));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(CodexOperationKind.Edit, state.OperationKind);
        Assert.Equal(@"E:\repo\PresenceRuntime.cs", state.ActiveFilePath);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.True(state.IsCompletedMutationDisplay);
    }

    [Theory]
    [InlineData((int)CodexOperationKind.Create)]
    [InlineData((int)CodexOperationKind.Delete)]
    public void Evaluate_CompletedFileMutationIsNotActiveAfterCompletion(
        int operationKindValue)
    {
        var operationKind = (CodexOperationKind)operationKindValue;
        var startedAt = Utc(27);
        var filePath = operationKind == CodexOperationKind.Create
            ? @"E:\repo\Created.cs"
            : @"E:\repo\Deleted.cs";
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: operationKind,
                targetPaths: [filePath]),
            Event(
                3,
                startedAt.AddMilliseconds(200),
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "call-1"),
            startedAt.AddSeconds(6));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(CodexOperationKind.Unknown, state.OperationKind);
        Assert.Null(state.ActiveFilePath);
        Assert.Equal(0, state.PendingOperationCount);
    }

    [Fact]
    public void Evaluate_TurnCompletionClosesPendingOperations()
    {
        var startedAt = Utc(30);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Command),
            Event(3, startedAt.AddSeconds(1), CodexActivityEventKind.TurnCompleted, turnId: "turn-1"));

        Assert.Equal(CodexTurnLifecycle.Completed, state.Lifecycle);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Null(state.ActiveFilePath);
        Assert.Equal(startedAt.AddSeconds(1), state.TerminalAtUtc);
    }

    [Fact]
    public void Evaluate_OldTurnEventsCannotReopenNewTurn()
    {
        var startedAt = Utc(40);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(2, startedAt.AddSeconds(1), CodexActivityEventKind.TurnCompleted, turnId: "turn-1"),
            Event(3, startedAt.AddSeconds(2), CodexActivityEventKind.TurnStarted, turnId: "turn-2"),
            Event(
                4,
                startedAt.AddSeconds(3),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "old-call",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\Old.cs"]));

        Assert.Equal("turn-2", state.TurnId);
        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Empty(state.MutationFilePaths);
    }

    [Fact]
    public void Evaluate_EventsAfterTerminalBarrierCannotReopenSameTurn()
    {
        var startedAt = Utc(45);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(2, startedAt.AddSeconds(1), CodexActivityEventKind.TurnCompleted, turnId: "turn-1"),
            Event(3, startedAt.AddSeconds(2), CodexActivityEventKind.Reasoning, turnId: "turn-1"),
            Event(
                4,
                startedAt.AddSeconds(3),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "late-call",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\Late.cs"]));

        Assert.Equal(CodexTurnLifecycle.Completed, state.Lifecycle);
        Assert.Equal("turn-1", state.TurnId);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Empty(state.MutationFilePaths);
    }

    [Fact]
    public void Evaluate_LatestMcpOperationOverridesEarlierThinkingSummary()
    {
        var startedAt = Utc(47);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddSeconds(1),
                CodexActivityEventKind.Reasoning,
                turnId: "turn-1",
                thinkingSummary: "Designing the MCP display"),
            Event(
                3,
                startedAt.AddSeconds(2),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-1",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "chrome_devtools"));

        Assert.Equal(CodexActivityEventKind.OperationStarted, state.TriggerEvent?.Kind);
        Assert.True(state.IsMcpOperation);
        Assert.Equal("chrome_devtools", state.McpServerName);
        Assert.Equal("Designing the MCP display", state.LatestThinkingSummary);
    }

    [Fact]
    public void Evaluate_CompletedMcpOperationRemainsVisibleUntilNextCodexEvent()
    {
        var startedAt = Utc(47);
        var completedAt = startedAt.AddSeconds(2);
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddSeconds(1),
                CodexActivityEventKind.Reasoning,
                turnId: "turn-1",
                thinkingSummary: "Preparing the browser check"),
            Event(
                3,
                completedAt,
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "mcp-call-1",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "chrome_devtools"),
            completedAt.AddSeconds(1));

        Assert.Equal(CodexActivityEventKind.OperationCompleted, state.TriggerEvent?.Kind);
        Assert.True(state.IsMcpOperation);
        Assert.Equal("chrome_devtools", state.McpServerName);
        Assert.Equal(["chrome_devtools"], state.ActiveMcpServerNames);
        Assert.Equal("MCP operation completed; waiting for next Codex event", state.Reason);
    }

    [Fact]
    public void Evaluate_CompletedResearchRemainsVisibleThroughTrailingToolOutput()
    {
        var startedAt = Utc(48);
        var completedAt = startedAt.AddSeconds(2);
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                completedAt,
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "research-call-1",
                operationKind: CodexOperationKind.Research),
            Event(
                3,
                completedAt.AddMilliseconds(1),
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "research-call-1"),
            completedAt.AddSeconds(1));

        Assert.Equal(CodexOperationKind.Research, state.OperationKind);
        Assert.Equal(CodexActivityEventKind.OperationCompleted, state.TriggerEvent?.Kind);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Equal("research operation completed; waiting for next Codex event", state.Reason);
    }

    [Fact]
    public void Evaluate_CompletedResearchExpiresAfterDisplayGrace()
    {
        var startedAt = Utc(49);
        var completedAt = startedAt.AddSeconds(2);
        var state = EvaluateAt(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                completedAt,
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "research-call-1",
                operationKind: CodexOperationKind.Research),
            Event(
                3,
                completedAt.AddMilliseconds(1),
                CodexActivityEventKind.OperationCompleted,
                turnId: "turn-1",
                callId: "research-call-1"),
            completedAt.AddSeconds(3));

        Assert.Equal(CodexOperationKind.Unknown, state.OperationKind);
        Assert.Equal(0, state.PendingOperationCount);
    }

    [Fact]
    public void Evaluate_LatestThinkingSummaryOverridesEarlierMcpOperation()
    {
        var startedAt = Utc(48);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddSeconds(1),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-1",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "chrome_devtools"),
            Event(
                3,
                startedAt.AddSeconds(2),
                CodexActivityEventKind.Reasoning,
                turnId: "turn-1",
                thinkingSummary: "Reviewing the MCP result"));

        Assert.Equal(CodexActivityEventKind.Reasoning, state.TriggerEvent?.Kind);
        Assert.True(state.IsMcpOperation);
        Assert.Equal("Reviewing the MCP result", state.LatestThinkingSummary);
    }

    [Fact]
    public void Evaluate_MultiplePendingMcpOperationsKeepsDistinctActiveServerNames()
    {
        var startedAt = Utc(49);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddSeconds(1),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-1",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "chrome_devtools"),
            Event(
                3,
                startedAt.AddSeconds(2),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-2",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "playwright"),
            Event(
                4,
                startedAt.AddSeconds(3),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-3",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "roblox_studio"),
            Event(
                5,
                startedAt.AddSeconds(4),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "mcp-call-4",
                operationKind: CodexOperationKind.Read,
                isMcpOperation: true,
                mcpServerName: "blender"));

        Assert.Equal("blender", state.McpServerName);
        Assert.Equal(
            ["blender", "roblox_studio", "playwright", "chrome_devtools"],
            state.ActiveMcpServerNames);
    }

    [Fact]
    public void Evaluate_NewStartWithReusedTurnIdCreatesNewLogicalTurn()
    {
        var startedAt = Utc(48);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(2, startedAt.AddSeconds(1), CodexActivityEventKind.TurnCompleted, turnId: "turn-1"),
            Event(3, startedAt.AddSeconds(2), CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                4,
                startedAt.AddSeconds(3),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "new-call",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\New.cs"]));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal("turn-1", state.TurnId);
        Assert.Equal(@"E:\repo\New.cs", state.ActiveFilePath);
    }

    [Fact]
    public void Evaluate_UnresolvedInputWinsOverPendingEdit()
    {
        var startedAt = Utc(50);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\Presence.cs"]),
            Event(3, startedAt.AddMilliseconds(200), CodexActivityEventKind.InputRequested, turnId: "turn-1", callId: "input-1"));

        Assert.Equal(CodexTurnLifecycle.WaitingForInput, state.Lifecycle);
        Assert.Equal(1, state.PendingOperationCount);
    }

    [Fact]
    public void Evaluate_MultipleTargetsDoNotInventActiveFile()
    {
        var startedAt = Utc(60);
        var state = Evaluate(
            Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
            Event(
                2,
                startedAt.AddMilliseconds(100),
                CodexActivityEventKind.OperationStarted,
                turnId: "turn-1",
                callId: "call-1",
                operationKind: CodexOperationKind.Edit,
                targetPaths: [@"E:\repo\A.cs", @"E:\repo\B.cs"]));

        Assert.Null(state.ActiveFilePath);
        Assert.Equal(2, state.MutationFilePaths.Count);
        Assert.Equal(2, state.PendingTargetPaths.Count);
    }

    [Fact]
    public void Evaluate_OpenTurnBecomesStalledAfterEffectiveSignalExpires()
    {
        var startedAt = Utc(70);
        var machine = new CodexActivityStateMachine(
            staleAfter: TimeSpan.FromSeconds(45),
            reasoningGrace: TimeSpan.FromSeconds(4));

        var state = machine.Evaluate(
            [Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1")],
            startedAt.AddSeconds(45));

        Assert.Equal(CodexTurnLifecycle.Stalled, state.Lifecycle);
        Assert.Contains("45 seconds", state.Reason);
    }

    [Fact]
    public void Evaluate_PendingOperationBecomesStalledWhenCompletionIsMissing()
    {
        var startedAt = Utc(75);
        var machine = new CodexActivityStateMachine(staleAfter: TimeSpan.FromSeconds(45));

        var state = machine.Evaluate(
            [
                Event(1, startedAt, CodexActivityEventKind.TurnStarted, turnId: "turn-1"),
                Event(
                    2,
                    startedAt.AddMilliseconds(100),
                    CodexActivityEventKind.OperationStarted,
                    turnId: "turn-1",
                    callId: "call-1",
                    operationKind: CodexOperationKind.Edit,
                    targetPaths: [@"E:\repo\Stalled.cs"])
            ],
            startedAt.AddSeconds(46));

        Assert.Equal(CodexTurnLifecycle.Stalled, state.Lifecycle);
        Assert.Equal(1, state.PendingOperationCount);
        Assert.Contains("completion", state.Reason);
    }

    private static CodexActivityState Evaluate(params CodexActivityEvent[] events)
    {
        return new CodexActivityStateMachine().Evaluate(events, events[^1].TimestampUtc.AddSeconds(1));
    }

    private static CodexActivityState EvaluateAt(
        CodexActivityEvent first,
        CodexActivityEvent second,
        CodexActivityEvent third,
        DateTime nowUtc)
    {
        return new CodexActivityStateMachine().Evaluate([first, second, third], nowUtc);
    }

    private static CodexActivityEvent Event(
        long sequence,
        DateTime timestampUtc,
        CodexActivityEventKind kind,
        string? turnId = null,
        string? callId = null,
        CodexOperationKind operationKind = CodexOperationKind.Unknown,
        IReadOnlyList<string>? targetPaths = null,
        string? thinkingSummary = null,
        bool isMcpOperation = false,
        string? mcpServerName = null)
    {
        return new CodexActivityEvent
        {
            Sequence = sequence,
            TimestampUtc = timestampUtc,
            Kind = kind,
            TurnId = turnId,
            CallId = callId,
            OperationKind = operationKind,
            IsMcpOperation = isMcpOperation,
            McpServerName = mcpServerName,
            ThinkingSummary = thinkingSummary,
            TargetPaths = targetPaths ?? [],
            Reason = kind.ToString(),
            Source = CodexActivitySource.SessionLog
        };
    }

    private static DateTime Utc(int second)
    {
        return new DateTime(2026, 9, 6, 0, 0, 0, DateTimeKind.Utc).AddSeconds(second);
    }
}
