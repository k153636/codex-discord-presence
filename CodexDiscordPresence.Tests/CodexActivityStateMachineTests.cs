namespace CodexDiscordPresence.Tests;

public sealed class CodexActivityStateMachineTests
{
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
        var state = Evaluate(
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
                callId: "call-1"));

        Assert.Equal(CodexTurnLifecycle.Open, state.Lifecycle);
        Assert.Equal(CodexOperationKind.Unknown, state.OperationKind);
        Assert.Equal(0, state.PendingOperationCount);
        Assert.Equal(0, state.PendingMutationCount);
        Assert.Null(state.ActiveFilePath);
        Assert.Single(state.MutationFilePaths);
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

    private static CodexActivityState Evaluate(params CodexActivityEvent[] events)
    {
        return new CodexActivityStateMachine().Evaluate(events, events[^1].TimestampUtc.AddSeconds(1));
    }

    private static CodexActivityEvent Event(
        long sequence,
        DateTime timestampUtc,
        CodexActivityEventKind kind,
        string? turnId = null,
        string? callId = null,
        CodexOperationKind operationKind = CodexOperationKind.Unknown,
        IReadOnlyList<string>? targetPaths = null)
    {
        return new CodexActivityEvent
        {
            Sequence = sequence,
            TimestampUtc = timestampUtc,
            Kind = kind,
            TurnId = turnId,
            CallId = callId,
            OperationKind = operationKind,
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
