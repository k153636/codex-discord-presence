using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexActivityEvidenceTests
{
    [Theory]
    [InlineData((int)CodexActivityEventKind.TurnStarted)]
    [InlineData((int)CodexActivityEventKind.Reasoning)]
    [InlineData((int)CodexActivityEventKind.OperationCompleted)]
    [InlineData((int)CodexActivityEventKind.InputResolved)]
    public void IsThinkingPhase_AcceptsReasoningBoundaryEvents(int eventKindValue)
    {
        var eventKind = (CodexActivityEventKind)eventKindValue;
        var state = new CodexActivityState
        {
            Lifecycle = CodexTurnLifecycle.Open,
            TriggerEvent = new CodexActivityEvent { Kind = eventKind }
        };

        Assert.True(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.AnalyzingProject, state));
        Assert.True(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.Planning, state));
    }

    [Fact]
    public void IsThinkingPhase_WithoutStateUsesKindAsFallbackEvidence()
    {
        Assert.True(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.AnalyzingProject, null));
        Assert.True(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.Planning, null));
    }

    [Fact]
    public void IsThinkingPhase_RejectsPendingUnknownOperation()
    {
        var state = new CodexActivityState
        {
            Lifecycle = CodexTurnLifecycle.Open,
            PendingOperationCount = 1,
            TriggerEvent = new CodexActivityEvent { Kind = CodexActivityEventKind.OperationStarted }
        };

        Assert.False(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.AnalyzingProject, state));
    }

    [Fact]
    public void IsThinkingPhase_RejectsStalledState()
    {
        var state = new CodexActivityState
        {
            Lifecycle = CodexTurnLifecycle.Stalled,
            TriggerEvent = new CodexActivityEvent { Kind = CodexActivityEventKind.TurnStarted }
        };

        Assert.False(CodexActivityEvidence.IsThinkingPhase(CodexActivityKind.AnalyzingProject, state));
    }

    [Theory]
    [InlineData(CodexActivityKind.ApplyingEdits)]
    [InlineData(CodexActivityKind.RunningCommand)]
    [InlineData(CodexActivityKind.ReadingFiles)]
    [InlineData(CodexActivityKind.Researching)]
    public void IsThinkingPhase_RejectsConcreteActivityKinds(CodexActivityKind activityKind)
    {
        var state = new CodexActivityState
        {
            Lifecycle = CodexTurnLifecycle.Open,
            TriggerEvent = new CodexActivityEvent { Kind = CodexActivityEventKind.Reasoning }
        };

        Assert.False(CodexActivityEvidence.IsThinkingPhase(activityKind, state));
    }
}
