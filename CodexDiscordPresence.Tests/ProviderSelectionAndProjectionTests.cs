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

    [Fact]
    public void ActivationGate_CurrentActiveProviderWinsOverNewerIdleProvider()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T01:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        var selected = gate.Select(
            [
                Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true),
                Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(1), isActive: false)
            ],
            initialTime);

        Assert.Equal(ProviderIds.Codex, selected?.ProviderId);

        selected = gate.Select(
            [
                Candidate(ProviderIds.Codex, observedAt: initialTime.AddMinutes(2), isActive: true),
                Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(3), isActive: false)
            ],
            initialTime.AddMinutes(3));

        Assert.Equal(ProviderIds.Codex, selected?.ProviderId);
    }

    [Fact]
    public void ActivationGate_NewerActiveProviderTakesOverWithoutCurrentProviderBecomingIdle()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T01:30:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        Assert.Equal(
            ProviderIds.Codex,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true),
                    Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddSeconds(-1), isActive: true)
                ],
                initialTime)?.ProviderId);

        var antigravityTime = initialTime.AddMinutes(1);
        Assert.Equal(
            ProviderIds.Antigravity,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true),
                    Candidate(ProviderIds.Antigravity, observedAt: antigravityTime, isActive: true)
                ],
                antigravityTime)?.ProviderId);
    }

    [Fact]
    public void ActivationGate_InitialSelectionUsesNewestActiveProvider()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T01:45:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        var selected = gate.Select(
            [
                Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true),
                Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(1), isActive: true)
            ],
            initialTime.AddMinutes(1));

        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);
    }

    [Fact]
    public void ActivationGate_ActiveAntigravityIgnoresCodexIdleChange()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T02:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        var selected = gate.Select(
            [
                Candidate(ProviderIds.Antigravity, observedAt: initialTime, isActive: true),
                Candidate(ProviderIds.Codex, observedAt: initialTime.AddMinutes(1), isActive: false)
            ],
            initialTime.AddMinutes(1));

        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);
    }

    [Fact]
    public void ActivationGate_RequiresNewActiveObservationAfterCurrentProviderStops()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T03:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        Assert.Equal(
            ProviderIds.Codex,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true),
                    Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(-1), isActive: true)
                ],
                initialTime)?.ProviderId);

        var inactiveTime = initialTime.AddMinutes(1);
        Assert.Equal(
            ProviderIds.Codex,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: inactiveTime, isActive: false),
                    Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(-1), isActive: true)
                ],
                inactiveTime)?.ProviderId);

        Assert.Equal(
            ProviderIds.Codex,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: inactiveTime.AddMinutes(1), isActive: false),
                    Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(-1), isActive: true)
                ],
                inactiveTime.AddMinutes(1))?.ProviderId);

        Assert.Equal(
            ProviderIds.Antigravity,
            gate.Select(
                [
                    Candidate(ProviderIds.Codex, observedAt: inactiveTime.AddMinutes(2), isActive: false),
                    Candidate(ProviderIds.Antigravity, observedAt: inactiveTime.AddMinutes(1), isActive: true)
                ],
                inactiveTime.AddMinutes(2))?.ProviderId);
    }

    [Fact]
    public void ActivationGate_IdleCandidateDoesNotSwitchAfterCurrentProviderStops()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T04:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        gate.Select(
            [Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true)],
            initialTime);

        var selected = gate.Select(
            [
                Candidate(ProviderIds.Codex, observedAt: initialTime.AddMinutes(1), isActive: false),
                Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(2), isActive: false)
            ],
            initialTime.AddMinutes(2));

        Assert.Equal(ProviderIds.Codex, selected?.ProviderId);
    }

    [Fact]
    public void ActivationGate_AllIdleCandidatesKeepThePreviousProvider()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T05:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        gate.Select(
            [Candidate(ProviderIds.Antigravity, observedAt: initialTime, isActive: true)],
            initialTime);

        var selected = gate.Select(
            [
                Candidate(ProviderIds.Antigravity, observedAt: initialTime.AddMinutes(1), isActive: false),
                Candidate(ProviderIds.Codex, observedAt: initialTime.AddMinutes(2), isActive: false)
            ],
            initialTime.AddMinutes(2));

        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);
    }

    [Fact]
    public void ActivationGate_MissingCurrentProviderWaitsForNewActiveObservation()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T05:30:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        Assert.Equal(
            ProviderIds.Antigravity,
            gate.Select(
                [Candidate(ProviderIds.Antigravity, observedAt: initialTime, isActive: true)],
                initialTime)?.ProviderId);

        var firstMissingTime = initialTime.AddMinutes(1);
        Assert.Null(
            gate.Select(
                [Candidate(ProviderIds.Codex, observedAt: firstMissingTime, isActive: true)],
                firstMissingTime));

        Assert.Equal(
            ProviderIds.Codex,
            gate.Select(
                [Candidate(ProviderIds.Codex, observedAt: firstMissingTime.AddMinutes(1), isActive: true)],
                firstMissingTime.AddMinutes(1))?.ProviderId);
    }

    [Fact]
    public void ActivationGate_UsesInactiveObservationTimeAsSwitchBoundary()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T05:45:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);

        gate.Select(
            [Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: true)],
            initialTime);

        var inactiveObservationTime = initialTime.AddMinutes(1);
        var selected = gate.Select(
            [
                Candidate(ProviderIds.Codex, observedAt: inactiveObservationTime, isActive: false),
                Candidate(ProviderIds.Antigravity, observedAt: inactiveObservationTime.AddSeconds(1), isActive: true)
            ],
            inactiveObservationTime.AddSeconds(5));

        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);
    }

    [Fact]
    public void ConversationSelector_PrefersActiveConversationOverNewerIdleConversation()
    {
        var active = Observation("conversation-a", ProviderAgentState.Working, "2026-09-12T06:00:00Z");
        var idle = Observation("conversation-b", ProviderAgentState.Idle, "2026-09-12T06:01:00Z");

        var selected = AntigravityConversationObservationSelector.Select([active, idle], "conversation-b");

        Assert.Equal("conversation-a", selected?.ConversationId);
    }

    [Fact]
    public void ConversationSelector_AllIdleConversationsKeepCurrentConversation()
    {
        var current = Observation("conversation-a", ProviderAgentState.Idle, "2026-09-12T07:00:00Z");
        var newer = Observation("conversation-b", ProviderAgentState.Idle, "2026-09-12T07:01:00Z");

        var selected = AntigravityConversationObservationSelector.Select([current, newer], "conversation-a");

        Assert.Equal("conversation-a", selected?.ConversationId);
    }

    [Fact]
    public void ConversationSelector_InitializingIsAnActiveConversation()
    {
        var initializing = Observation("conversation-a", ProviderAgentState.Initializing, "2026-09-12T08:00:00Z");

        Assert.True(AntigravityConversationObservationSelector.IsActive(initializing.AgentState));
    }

    [Fact]
    public void ConversationSelector_HookLifecycleWinsOverNewerStatusLineIdle()
    {
        var hook = Observation("conversation-a", ProviderAgentState.Thinking, "2026-09-12T08:00:00Z");
        var statusLine = Observation("conversation-a", ProviderAgentState.Idle, "2026-09-12T08:01:00Z");

        var selected = AntigravityConversationObservationSelector.Select(
            [hook],
            [statusLine],
            currentConversationId: null);

        Assert.Equal(ProviderAgentState.Thinking, selected?.AgentState);
        Assert.Equal(hook.ObservedAtUtc, selected?.ObservedAtUtc);
    }

    [Fact]
    public void ConversationSelector_HookLifecycleUsesStatusLineMetadataAsFallback()
    {
        var hook = new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            DateTimeOffset.Parse("2026-09-12T08:00:00Z"),
            ProviderAgentState.Working,
            null,
            null,
            "conversation-a");
        var statusLine = new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            DateTimeOffset.Parse("2026-09-12T08:01:00Z"),
            ProviderAgentState.Idle,
            new ProviderModelObservation("model-id", "Model display"),
            new ProviderWorkspaceObservation(@"C:\repo", @"C:\repo", @"C:\repo"),
            "conversation-a",
            ProviderExecutionMode.Planning,
            new ProviderContextWindowObservation(10, 20),
            2);

        var selected = AntigravityConversationObservationSelector.Select(
            [hook],
            [statusLine],
            currentConversationId: null);

        Assert.Equal(ProviderAgentState.Working, selected?.AgentState);
        Assert.Equal("model-id", selected?.Model?.Id);
        Assert.Equal("repo", selected?.Workspace?.WorkspaceName);
        Assert.Equal(ProviderExecutionMode.Planning, selected?.ExecutionMode);
        Assert.Equal(30, selected?.ContextWindow?.TotalTokens);
        Assert.Equal(2, selected?.ActiveSubagentCount);
    }

    [Fact]
    public void AntigravityRuntimeState_KeepsActivityCachePerConversation()
    {
        var state = new AntigravityRuntimeState();
        var conversationA = state.GetPresenceCache("conversation-a");
        conversationA.LastActivity = AntigravityPresenceProjection.Build(
            Observation("conversation-a", ProviderAgentState.Working, "2026-09-12T09:00:00Z")).Activity;

        var conversationB = state.GetPresenceCache("conversation-b");

        Assert.Null(conversationB.LastActivity);
        Assert.Same(conversationA, state.GetPresenceCache("conversation-a"));
        Assert.Equal("conversation-a", state.CurrentConversationId);
    }

    [Theory]
    [InlineData((int)ProviderAgentState.Idle, CodexActivityKind.Ready, false)]
    [InlineData((int)ProviderAgentState.Thinking, CodexActivityKind.AnalyzingProject, true)]
    [InlineData((int)ProviderAgentState.Working, CodexActivityKind.AnalyzingProject, true)]
    [InlineData((int)ProviderAgentState.ToolUse, CodexActivityKind.RunningCommand, false)]
    [InlineData((int)ProviderAgentState.Initializing, CodexActivityKind.AnalyzingProject, true)]
    [InlineData((int)ProviderAgentState.Unknown, CodexActivityKind.Ready, false)]
    public void Build_ProjectsEveryAgentStateToProviderOwnedMeaning(
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

        Assert.Equal(expectedActivityKind, projection.Activity.ActivityKind);
        Assert.Equal(expectedThinking, projection.Activity.IsThinking);
        Assert.Equal(ActivityProvenance.Observed, projection.Activity.ActivityProvenance);
        var expectedProviderState = agentState switch
        {
            ProviderAgentState.Idle => "idle",
            ProviderAgentState.Thinking => "thinking",
            ProviderAgentState.Working => "working",
            ProviderAgentState.ToolUse => "tool_use",
            ProviderAgentState.Initializing => "initializing",
            _ => "unknown"
        };
        Assert.Equal(expectedProviderState, projection.Activity.ProviderState);
        Assert.Null(projection.Activity.PartySize);
    }

    [Fact]
    public void Build_UsesOnlyExplicitActiveSubagentCountForParty()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Working,
                "2026-09-11T04:05:06Z",
                activeSubagentCount: 2));

        Assert.Equal(2, projection.Activity.ActiveSubagentCount);
        Assert.Equal(3, projection.Activity.PartySize);
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
        Assert.Null(projection.Activity.LastObservedAt);
        Assert.Null(projection.Activity.ActiveTurnId);
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
            projection.Activity,
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
    public void Build_UsesStructuredToolMetadataForReadingActivity()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.ToolUse,
                "2026-09-12T09:00:00Z") with
            {
                Operation = new ProviderOperationObservation(
                    CodexOperationKind.Read,
                    "view_file",
                    "Reading source",
                    "Source file",
                    @"C:\repo\src\Program.cs")
            });

        Assert.Equal(CodexActivityKind.ReadingFiles, projection.Activity.ActivityKind);
        Assert.False(projection.Activity.IsThinking);
        Assert.Equal("Reading source", projection.Activity.ActiveActivityDescription);
        Assert.Equal([@"C:\repo\src\Program.cs"], projection.Activity.ActivityFilePaths);
        Assert.Equal(CodexActivityEventKind.OperationStarted, projection.Activity.LatestActivityEventKind);
        Assert.Null(projection.Activity.LatestThinkingSummary);
    }

    [Fact]
    public void Build_ConfirmationEvidenceOverridesToolActivityWithWaiting()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Working,
                "2026-09-12T09:00:00Z") with
            {
                Operation = new ProviderOperationObservation(
                    CodexOperationKind.Research,
                    "read_url_content",
                    "Reading pricing page"),
                IsWaitingForInput = true
            });

        Assert.Equal(CodexActivityKind.WaitingForInput, projection.Activity.ActivityKind);
        Assert.False(projection.Activity.IsThinking);
        Assert.Equal(CodexActivityEventKind.InputRequested, projection.Activity.LatestActivityEventKind);
    }

    [Fact]
    public void Build_IdleStateDoesNotResurfaceCompletedOperation()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Idle,
                "2026-09-12T09:00:00Z") with
            {
                Operation = new ProviderOperationObservation(
                    CodexOperationKind.Research,
                    "search_web",
                    "Searching the web",
                    IsCompleted: true)
            });

        Assert.Equal(CodexActivityKind.Ready, projection.Activity.ActivityKind);
        Assert.Equal(CodexActivityEventKind.TurnCompleted, projection.Activity.LatestActivityEventKind);
        Assert.Null(projection.Activity.ActiveActivityDescription);
        Assert.Equal(0, projection.Activity.PendingOperationCount);
    }

    [Fact]
    public void Build_ZeroContextTokensAreOmittedFromDetails()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation("conversation-a", ProviderAgentState.Working, "2026-09-12T09:00:00Z") with
            {
                ContextWindow = new ProviderContextWindowObservation(0, 0)
            });

        Assert.Null(projection.TotalTokens);
    }

    [Fact]
    public void Render_UsesAntigravityResearchDescriptionWithoutThinkingSummary()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.ToolUse,
                "2026-09-12T09:00:00Z") with
            {
                Operation = new ProviderOperationObservation(
                    CodexOperationKind.Research,
                    "search_web",
                    "Searching the web")
            });
        var context = CreatePresenceContext(projection);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Searching the web", presence.State);
        Assert.Null(projection.Activity.LatestThinkingSummary);
    }

    [Fact]
    public void Render_AntigravityWorkingWithoutOperationUsesWorkingLabel()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Working,
                "2026-09-12T09:00:00Z"));
        var context = CreatePresenceContext(projection);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Working", presence.State);
        Assert.Null(projection.Activity.LatestActivityEventKind);
        Assert.Null(projection.Activity.LatestThinkingSummary);
    }

    [Fact]
    public void Render_AntigravityIdleWithinGracePeriodUsesWaitingLabel()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Idle,
                DateTimeOffset.UtcNow.AddMinutes(-4).ToString("O")));
        var context = CreatePresenceContext(projection);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Waiting", presence.State);
    }

    [Fact]
    public void Render_AntigravityIdleAfterGracePeriodUsesIdlingLabel()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Idle,
                DateTimeOffset.UtcNow.AddMinutes(-6).ToString("O")));
        var context = CreatePresenceContext(projection);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Idling", presence.State);
    }

    [Fact]
    public void Render_AntigravityThinkingUsesThinkingLabelWithoutSummary()
    {
        var projection = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Thinking,
                DateTimeOffset.UtcNow.ToString("O")));
        var context = CreatePresenceContext(projection);

        var presence = new PresenceTemplateRenderer().Render(
            new PresenceTemplateOptions { State = "{ActivityLine}" },
            context);

        Assert.Equal("Thinking", presence.State);
        Assert.Null(projection.Activity.LatestThinkingSummary);
    }

    [Fact]
    public void Build_PreservesActivityStartWhileAntigravityStateRemainsIdle()
    {
        var first = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Idle,
                "2026-09-12T09:00:00Z"));
        var second = AntigravityPresenceProjection.Build(
            Observation(
                "conversation-a",
                ProviderAgentState.Idle,
                "2026-09-12T09:01:00Z"),
            first.Activity);

        Assert.Equal(first.Activity.ActivityStartedAt, second.Activity.ActivityStartedAt);
        Assert.Equal(
            second.Activity.LastObservedAt,
            DateTimeOffset.Parse("2026-09-12T09:01:00Z").UtcDateTime);
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

    [Fact]
    public void Build_UsesCompleteGeminiIdentifierWhenDisplayNameOmitsVariant()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.UtcNow,
                ProviderAgentState.Idle,
                new ProviderModelObservation("gemini-3.8-flash-high", "Gemini 3.8 Flash"),
                null,
                null));

        Assert.Equal("gemini 3.8 flash high", projection.ModelName);
    }

    [Fact]
    public void Build_PreservesCompleteGeminiDisplayName()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.UtcNow,
                ProviderAgentState.Idle,
                new ProviderModelObservation(
                    "gemini-3.8-flash-high",
                    "Gemini 3.8 Flash (High)"),
                null,
                null));

        Assert.Equal("Gemini 3.8 Flash (High)", projection.ModelName);
    }

    [Fact]
    public void Build_PreservesClaudeThinkingAsNativeModelIdentity()
    {
        var projection = AntigravityPresenceProjection.Build(
            new ProviderObservation(
                ProviderObservationSource.AntigravityCli,
                DateTimeOffset.UtcNow,
                ProviderAgentState.Idle,
                new ProviderModelObservation(
                    "Claude Sonnet 4.6 (Thinking)",
                    "Claude Sonnet 4.6 (Thinking)"),
                null,
                null));

        Assert.Equal("Claude Sonnet 4.6 (Thinking)", projection.ModelName);
    }

    private static ProviderSelectionCandidate Candidate(
        string providerId,
        bool isEnabled = true,
        bool isAvailable = true,
        bool hasProjectPath = false,
        bool isProjectMatch = false,
        DateTimeOffset? observedAt = null,
        int detectionStrength = 0,
        bool isActive = false)
    {
        return new ProviderSelectionCandidate(
            providerId,
            isEnabled,
            isAvailable,
            observedAt,
            hasProjectPath,
            isProjectMatch,
            detectionStrength,
            isActive);
    }

    private static ProviderObservation Observation(
        string conversationId,
        ProviderAgentState agentState,
        string observedAt,
        int? activeSubagentCount = null)
    {
        return new ProviderObservation(
            ProviderObservationSource.AntigravityCli,
            DateTimeOffset.Parse(observedAt),
            agentState,
            null,
            null,
            conversationId,
            ProviderExecutionMode.Unknown,
            null,
            activeSubagentCount);
    }

    private static PresenceContext CreatePresenceContext(
        AntigravityPresenceProjectionResult projection)
    {
        return new PresenceContext(
            projection.ModelName,
            projection.Activity,
            new ProjectSnapshot("repo", @"C:\repo", null, null, 0, 0, 0, []),
            new GitSnapshot(false, 0, null),
            new SessionSnapshot(DateTime.UtcNow, TimeSpan.Zero),
            new TokenUsageSnapshot(null, null))
        {
            ProviderId = ProviderIds.Antigravity
        };
    }
}
