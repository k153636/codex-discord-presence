using System.Text.Json;

namespace CodexDiscordPresence.Tests;

public sealed class AntigravityDetectionTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Freshness = TimeSpan.FromMinutes(2);
    private static readonly string CurrentProject = Path.Combine(Path.GetTempPath(), "CodexProject");
    private static readonly string OtherProject = Path.Combine(Path.GetTempPath(), "別のプロジェクト");

    [Fact]
    public void Detection_ActiveDifferentProject_ReachesCentralGateWithoutDisablingProjectMatching()
    {
        WithStore(store =>
        {
            Assert.True(store.TryAppend(Observation("same-project", ProviderAgentState.Idle, Now), CurrentProject));
            Assert.True(store.TryAppend(Observation("other-project", ProviderAgentState.Working, Now.AddSeconds(1)), OtherProject));
            var observations = PresenceRuntime.ReadFreshAntigravityObservations(store, Now.AddSeconds(2), Freshness);
            Assert.Equal(2, observations.Count);
            var selected = AntigravityConversationObservationSelector.Select(observations, null)!;
            Assert.Equal("other-project", selected.ConversationId);
            Assert.False(AntigravityStatusLineEventStore.MatchesProjectPath(selected, CurrentProject));
            Assert.True(AntigravityStatusLineEventStore.MatchesProjectPath(selected, OtherProject));

            var gate = new ProviderActivationGate(ProviderIds.Codex);
            var codex = new ProviderSelectionCandidate(ProviderIds.Codex, true, true, Now, true, true,
                IsActive: true, ActivityStartedAtUtc: Now);
            Assert.Equal(ProviderIds.Codex, gate.Select([codex], Now)!.ProviderId);
            var antigravity = new ProviderSelectionCandidate(ProviderIds.Antigravity, true, true, selected.ObservedAtUtc,
                true, false, IsActive: true, ActivityStartedAtUtc: selected.ObservedAtUtc);
            Assert.Equal(ProviderIds.Codex, gate.Select([codex, antigravity], Now.AddSeconds(5))!.ProviderId);
            Assert.Equal(ProviderIds.Antigravity, gate.Select([codex, antigravity], Now.AddSeconds(31))!.ProviderId);

            // Project-filtered consumers retain their strict, opaque-key behavior.
            Assert.True(store.TryReadLatest(CurrentProject, out var matching));
            Assert.Equal("same-project", matching!.ConversationId);
        });
    }

    [Theory]
    [InlineData(-121, "Working")]
    [InlineData(1, "Working")]
    [InlineData(0, "Unknown")]
    public void Detection_StaleFutureOrUnknownDifferentProject_DoesNotBecomeCandidate(int offset, string state)
    {
        WithStore(store =>
        {
            Assert.True(store.TryAppend(Observation("other", Enum.Parse<ProviderAgentState>(state), Now.AddSeconds(offset)), OtherProject));
            Assert.Empty(PresenceRuntime.ReadFreshAntigravityObservations(store, Now, Freshness));
        });
    }

    [Fact]
    public void Detection_IdleDifferentProject_DoesNotTakeOverCurrentActiveProvider()
    {
        WithStore(store =>
        {
            Assert.True(store.TryAppend(Observation("other", ProviderAgentState.Idle, Now.AddSeconds(1)), OtherProject));
            var observation = Assert.Single(PresenceRuntime.ReadFreshAntigravityObservations(store, Now.AddSeconds(2), Freshness));
            var gate = new ProviderActivationGate(ProviderIds.Codex);
            var codex = new ProviderSelectionCandidate(ProviderIds.Codex, true, true, Now, true, true, IsActive: true);
            gate.Select([codex], Now);
            var idle = new ProviderSelectionCandidate(ProviderIds.Antigravity, true, true, observation.ObservedAtUtc,
                true, false, IsActive: AntigravityConversationObservationSelector.IsActive(observation.AgentState));
            Assert.Equal(ProviderIds.Codex, gate.Select([codex, idle], Now.AddSeconds(31))!.ProviderId);
        });
    }

    [Fact]
    public void ProjectIdentity_RemainsOpaqueAndNormalizesCaseAndTrailingSeparator()
    {
        WithStore(store =>
        {
            Assert.True(store.TryAppend(Observation("other", ProviderAgentState.Working, Now), OtherProject));
            Assert.True(store.TryReadLatest(out var observation));
            Assert.NotNull(observation!.ProjectKey);
            Assert.True(AntigravityStatusLineEventStore.MatchesProjectPath(observation, OtherProject.ToUpperInvariant() + "\\"));
            Assert.False(AntigravityStatusLineEventStore.MatchesProjectPath(observation, CurrentProject));
            Assert.DoesNotContain("ProjectKey", JsonSerializer.Serialize(observation));
            Assert.False(AntigravityStatusLineEventStore.MatchesProjectPath(observation with { ProjectKey = null }, OtherProject));
        });
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Context_UsesLocalGitAndFilesOnlyForMatchingProject(bool matching)
    {
        var projection = AntigravityPresenceProjection.Build(Observation("other", ProviderAgentState.Working, Now));
        var project = new ProjectSnapshot("codex-project", CurrentProject, "private.txt", Path.Combine(CurrentProject, "private.txt"),
            100, 90, 500, [new("private.txt", Path.Combine(CurrentProject, "private.txt"), Now.UtcDateTime)]);
        var git = new GitSnapshot(true, 7, "private commit", 2, 1);
        var session = new SessionSnapshot(Now.UtcDateTime, TimeSpan.FromHours(1));
        var context = PresenceRuntime.BuildAntigravityPresenceContext(session, projection, project, git, matching);
        Assert.Equal(ProviderIds.Antigravity, context.ProviderId);
        Assert.Same(session, context.Session);
        Assert.Equal(projection.ModelName, context.ModelName);
        if (matching)
        {
            Assert.Same(project, context.Project);
            Assert.Same(git, context.Git);
        }
        else
        {
            Assert.Equal("other-project", context.Project.Name);
            Assert.Empty(context.Project.Path);
            Assert.Empty(context.Project.RecentFiles);
            Assert.Null(context.Project.RecentFilePath);
            Assert.False(context.Git.IsGitRepository);
            Assert.Equal(0, context.Git.ChangedFileCount);
            Assert.Null(context.Git.LatestCommitMessage);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Selector_SameConversationInDifferentProjects_DoesNotMergeHookActivityAndStatusLineMetadata(bool newerHook)
    {
        var hook = Observation("shared-id", ProviderAgentState.ToolUse, newerHook ? Now.AddSeconds(1) : Now) with
        {
            ProjectKey = new string('A', 64), Model = null, Workspace = null,
            Operation = new(CodexOperationKind.Read, TargetPath: "hook.txt")
        };
        var status = Observation("shared-id", ProviderAgentState.Working, newerHook ? Now : Now.AddSeconds(1)) with
        {
            ProjectKey = new string('B', 64)
        };
        var selected = AntigravityConversationObservationSelector.Select([hook], [status], null);
        Assert.Same(newerHook ? hook : status, selected);
    }

    [Fact]
    public void Context_DifferentProject_PreservesDirectFileEvidenceWithoutBorrowingLocalFilesOrGit()
    {
        var observation = Observation("other", ProviderAgentState.ToolUse, Now) with
        {
            Operation = new(CodexOperationKind.Edit, TargetPath: Path.Combine(OtherProject, "observed.cs"))
        };
        var projection = AntigravityPresenceProjection.Build(observation);
        var context = PresenceRuntime.BuildAntigravityPresenceContext(new(Now.UtcDateTime, TimeSpan.Zero), projection,
            new("codex-project", CurrentProject, "private.txt", Path.Combine(CurrentProject, "private.txt"), 100, 90, 500, []),
            new(true, 7, "private commit"), false);
        var rendered = new PresenceTemplateRenderer(() => Now.UtcDateTime).Render(new()
        {
            Details = "{ProjectName}", State = "{ActivityLine}"
        }, context);
        Assert.Equal("other-project", rendered.Details);
        Assert.Equal("Editing observed.cs", rendered.State);
        Assert.DoesNotContain(CurrentProject, JsonSerializer.Serialize(rendered));
        Assert.Null(context.Git.LatestCommitMessage);
    }

    private static ProviderObservation Observation(string conversation, ProviderAgentState state, DateTimeOffset time) =>
        new(ProviderObservationSource.AntigravityCli, time, state, new("gemini-test", "gemini-test"),
            new(null, "other-project", "other-project"), conversation);

    private static void WithStore(Action<AntigravityStatusLineEventStore> action)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AntigravityDetectionTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try { action(new(Path.Combine(directory, "events.jsonl"))); }
        finally { Directory.Delete(directory, true); }
    }
}
