namespace CodexDiscordPresence.Tests;

public sealed class UpdateRestartPolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(CodexActivityKind.Ready, true)]
    [InlineData(CodexActivityKind.WaitingForInput, true)]
    [InlineData(CodexActivityKind.Offline, true)]
    [InlineData(CodexActivityKind.Stalled, false)]
    [InlineData(CodexActivityKind.RunningCommand, false)]
    [InlineData(CodexActivityKind.ApplyingEdits, false)]
    [InlineData(CodexActivityKind.Planning, false)]
    public void CanRestart_RequiresFreshIdleEvidenceAndClosedDashboard(CodexActivityKind kind, bool expected)
    {
        var state = new PresenceRuntimeState();
        state.PublishDashboardSnapshot(new PresenceDashboardSnapshot(AppProfileKind.Codex, null, null,
            new RenderedPresence("", "", null, "", [], Now, kind, RunningCommandKind.Unknown, ""), null, true, Now));
        Assert.Equal(expected, UpdateRestartPolicy.CanRestart(state, false, Now));
        Assert.False(UpdateRestartPolicy.CanRestart(state, true, Now));
        Assert.False(UpdateRestartPolicy.CanRestart(state, false, Now.AddSeconds(16)));
    }

    [Fact]
    public void CanRestart_UnknownStartupStateDoesNotCountAsIdle()
    {
        Assert.False(UpdateRestartPolicy.CanRestart(new PresenceRuntimeState(), false, Now));
        var disabled = new PresenceRuntimeState { Enabled = false };
        Assert.True(UpdateRestartPolicy.CanRestart(disabled, false, Now));
        Assert.False(UpdateRestartPolicy.CanRestart(disabled, true, Now));
    }

    [Fact]
    public void RestartArguments_PreserveSpacedArgumentsAndReplacePreviousTimestamp()
    {
        var started = Now.AddHours(-2);
        var arguments = UpdateRestartArguments.Build(["--project", "E:\\a project", "--interval", "3",
            "--resume-session-start", Now.ToString("O")], started);
        Assert.Equal(["--project", "E:\\a project", "--interval", "3", "--resume-session-start", started.ToString("O")], arguments);
        Assert.Equal(started, UpdateRestartArguments.ReadSessionStart(arguments, Now));
    }

    [Theory]
    [InlineData("not a timestamp")]
    [InlineData("2027-10-09T12:00:00.0000000Z")]
    [InlineData("2020-10-09T12:00:00.0000000Z")]
    [InlineData("2026-10-09T12:00:00.0000000+09:00")]
    public void RestartArguments_InvalidTimestampStartsANewSession(string value)
    {
        Assert.Equal(Now, UpdateRestartArguments.ReadSessionStart(["--resume-session-start", value], Now));
    }
}
