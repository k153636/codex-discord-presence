using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class ProviderSelectionAndProjectionTests
{
    [Fact]
    public void Select_ExcludesDisabledAndUnavailableCandidates()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("disabled", isEnabled: false, isAvailable: true, observedAt: now),
                Candidate("unavailable", isEnabled: true, isAvailable: false, observedAt: now),
                Candidate("usable", isEnabled: true, isAvailable: true, observedAt: now.AddSeconds(-1))
            ]);

        Assert.Equal("usable", selected?.ProviderId);
    }

    [Fact]
    public void Select_PrefersProjectMatchOverNewerNonMatch()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("matching", hasProjectPath: true, isProjectMatch: true, observedAt: now.AddMinutes(-1)),
                Candidate("newer-other-project", hasProjectPath: true, isProjectMatch: false, observedAt: now)
            ]);

        Assert.Equal("matching", selected?.ProviderId);
    }

    [Fact]
    public void Select_DoesNotTreatProjectMatchWithoutAProjectPathAsAValidatedMatch()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("unvalidated-match", hasProjectPath: false, isProjectMatch: true, observedAt: now.AddSeconds(-1)),
                Candidate("validated-project", hasProjectPath: true, isProjectMatch: false, observedAt: now)
            ]);

        Assert.Equal("validated-project", selected?.ProviderId);
    }

    [Fact]
    public void Select_PrefersLatestObservationWithinMatchingCandidates()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("older", hasProjectPath: true, isProjectMatch: true, observedAt: now.AddSeconds(-1)),
                Candidate("latest", hasProjectPath: true, isProjectMatch: true, observedAt: now)
            ]);

        Assert.Equal("latest", selected?.ProviderId);
    }

    [Fact]
    public void Select_UsesDetectionStrengthWhenObservationTimeIsTied()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("weak", observedAt: observedAt, detectionStrength: 10),
                Candidate("strong", observedAt: observedAt, detectionStrength: 20)
            ]);

        Assert.Equal("strong", selected?.ProviderId);
    }

    [Fact]
    public void Select_UsesCurrentProviderWhenEvidenceIsTied()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: "current",
            [
                Candidate("other", observedAt: observedAt),
                Candidate("current", observedAt: observedAt)
            ]);

        Assert.Equal("current", selected?.ProviderId);
    }

    [Fact]
    public void Select_UsesProviderIdAsDeterministicFinalTieBreak()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var candidates = new[]
        {
            Candidate("zeta", observedAt: observedAt),
            Candidate("alpha", observedAt: observedAt)
        };

        var selected = ProviderSelectionPolicy.Select(null, candidates);

        Assert.Equal("alpha", selected?.ProviderId);
    }

    [Fact]
    public void Select_ReturnsNullWhenNoCandidateIsEligible()
    {
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: "codex",
            [Candidate("codex", isEnabled: false, isAvailable: false)]);

        Assert.Null(selected);
    }

    [Fact]
    public void Select_TreatsMissingObservedTimeAsOlderThanObservedCandidate()
    {
        var selected = ProviderSelectionPolicy.Select(
            currentProviderId: null,
            [
                Candidate("missing-time", observedAt: null),
                Candidate("observed", observedAt: DateTimeOffset.UtcNow)
            ]);

        Assert.Equal("observed", selected?.ProviderId);
    }

    [Theory]
    [InlineData((int)ProviderAgentState.Idle, CodexActivityKind.Ready, false)]
    [InlineData((int)ProviderAgentState.Thinking, CodexActivityKind.AnalyzingProject, true)]
    [InlineData((int)ProviderAgentState.Working, CodexActivityKind.ApplyingEdits, false)]
    [InlineData((int)ProviderAgentState.ToolUse, CodexActivityKind.RunningCommand, false)]
    [InlineData((int)ProviderAgentState.Initializing, CodexActivityKind.AnalyzingProject, true)]
    [InlineData((int)ProviderAgentState.Unknown, CodexActivityKind.Ready, false)]
    public void Build_ProjectsEveryAgentStateToStableCodexMeaning(
        int agentStateValue,
        CodexActivityKind expectedActivityKind,
        bool expectedThinking)
    {
        var agentState = (ProviderAgentState)agentStateValue;
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.Parse("2026-09-11T04:05:06Z"),
                agentState,
                new ProviderModelObservation("model-id", "Model display"),
                new ProviderWorkspaceObservation(
                    @"C:\Users\private\repo",
                    @"C:\Users\private\repo",
                    @"C:\Users\private\repo"),
                "conversation-id"));

        Assert.Equal(expectedActivityKind, projection.Snapshot.ActivityKind);
        Assert.Equal(expectedThinking, projection.Snapshot.IsThinking);
        Assert.Equal(CodexProcessDetectionKind.SessionActivity, projection.Snapshot.DetectionKind);
        Assert.Equal(ActivityProvenance.Observed, projection.Snapshot.ActivityProvenance);
    }

    [Fact]
    public void Build_UsesSafeFallbacksWithoutLeakingPathOrSecretValues()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                default,
                ProviderAgentState.Unknown,
                new ProviderModelObservation(
                    @"C:\Users\private\secret-token",
                    @"C:\Users\private\secret-token"),
                new ProviderWorkspaceObservation(
                    @"C:\Users\private\repo",
                    @"C:\Users\private\repo",
                    @"C:\Users\private\repo"),
                @"C:\Users\private\conversation"));

        Assert.Equal("Unknown model", projection.ModelName);
        Assert.Null(projection.ConversationId);
        Assert.Equal("repo", projection.WorkspaceName);
        Assert.Null(projection.Snapshot.LastObservedAt);
        Assert.Null(projection.Snapshot.ActiveTurnId);
        Assert.DoesNotContain("secret-token", projection.ModelName, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_UsesSharedActivityLabelForAntigravityProjection()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.UtcNow,
                ProviderAgentState.ToolUse,
                new ProviderModelObservation("model-id", "Model display"),
                null,
                null));
        var context = new PresenceContext(
            projection.ModelName,
            projection.Snapshot,
            new ProjectSnapshot("repo", @"C:\repo", null, null, 0, 0, 0, []),
            new GitSnapshot(false, 0, null),
            new SessionSnapshot(DateTime.UtcNow, TimeSpan.Zero),
            new TokenUsageSnapshot(null, null))
        {
            ProviderId = ProviderIds.Antigravity
        };

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions
            {
                State = "{ActivityLine}",
                RunningCommandText = "Provider tools"
            },
            context);

        Assert.Equal("Provider tools", presence.State);
    }

    [Fact]
    public void Build_UsesModelIdWhenDisplayNameIsMissing()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.UtcNow,
                ProviderAgentState.Idle,
                new ProviderModelObservation("model-id", null),
                null,
                null));

        Assert.Equal("model-id", projection.ModelName);
        Assert.Null(projection.WorkspaceName);
        Assert.Null(projection.ConversationId);
    }

    private static ProviderSelectionCandidate Candidate(
        string providerId,
        bool isEnabled = true,
        bool isAvailable = true,
        bool hasProjectPath = false,
        bool isProjectMatch = false,
        DateTimeOffset? observedAt = null,
        int detectionStrength = 0)
    {
        return new ProviderSelectionCandidate(
            providerId,
            isEnabled,
            isAvailable,
            observedAt,
            hasProjectPath,
            isProjectMatch,
            detectionStrength);
    }
}
