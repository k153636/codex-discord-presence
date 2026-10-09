using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class ProviderActivationGateTests
{
    [Fact]
    public void MinimumSwitchInterval_IsFifteenSeconds() =>
        Assert.Equal(TimeSpan.FromSeconds(15), ProviderActivationGate.MinimumSwitchInterval);

    [Fact]
    public void Select_UsesActivityStartToAvoidSwitchingBackToAnOlderActiveProvider()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T01:35:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        Assert.Equal(
            ProviderIds.Antigravity,
            SelectAndAcknowledge(gate,
                [
                    Candidate(
                        ProviderIds.Antigravity,
                        observedAt: initialTime,
                        isActive: true,
                        activityStartedAt: initialTime),
                    Candidate(
                        ProviderIds.Codex,
                        observedAt: initialTime.AddSeconds(-1),
                        isActive: false)
                ],
                initialTime)?.ProviderId);

        var codexActivityStart = initialTime.AddMinutes(1);
        Assert.Equal(
            ProviderIds.Codex,
            SelectAndAcknowledge(gate,
                [
                    Candidate(
                        ProviderIds.Antigravity,
                        observedAt: initialTime.AddMinutes(2),
                        isActive: true,
                        activityStartedAt: initialTime),
                    Candidate(
                        ProviderIds.Codex,
                        observedAt: initialTime.AddMinutes(2),
                        isActive: true,
                        activityStartedAt: codexActivityStart)
                ],
                initialTime.AddMinutes(2))?.ProviderId);

        Assert.Equal(
            ProviderIds.Codex,
            SelectAndAcknowledge(gate,
                [
                    Candidate(
                        ProviderIds.Antigravity,
                        observedAt: initialTime.AddMinutes(3),
                        isActive: true,
                        activityStartedAt: initialTime),
                    Candidate(
                        ProviderIds.Codex,
                        observedAt: initialTime.AddMinutes(3),
                        isActive: true,
                        activityStartedAt: codexActivityStart)
                ],
                initialTime.AddMinutes(3))?.ProviderId);
    }

    [Fact]
    public void Select_SwitchesToCodex_WhenCurrentAntigravityBecomesIdleAndCodexBecomesActive()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T21:39:52Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        // 1. Antigravity is active
        var selected = SelectAndAcknowledge(gate,
            [
                Candidate(ProviderIds.Antigravity, observedAt: initialTime, isActive: true, activityStartedAt: initialTime),
                Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: false)
            ],
            initialTime);
        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);

        // 2. Antigravity transitions to idle at 21:40:09
        var idleTime = DateTimeOffset.Parse("2026-09-12T21:40:09Z");
        selected = SelectAndAcknowledge(gate,
            [
                Candidate(ProviderIds.Antigravity, observedAt: idleTime, isActive: false),
                Candidate(ProviderIds.Codex, observedAt: idleTime, isActive: false)
            ],
            idleTime);
        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);

        // 3. Codex starts thinking at 21:45:13
        var codexStartTime = DateTimeOffset.Parse("2026-09-12T21:45:13Z");
        selected = SelectAndAcknowledge(gate,
            [
                Candidate(ProviderIds.Antigravity, observedAt: codexStartTime, isActive: false),
                Candidate(ProviderIds.Codex, observedAt: codexStartTime, isActive: true, activityStartedAt: codexStartTime)
            ],
            codexStartTime);
        Assert.Equal(ProviderIds.Codex, selected?.ProviderId);
    }

    [Fact]
    public void Select_ClaudeActive_FollowsNewCodexEventFromAnOlderTask()
    {
        var start = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.ClaudeCode);
        var claude = Candidate(ProviderIds.ClaudeCode, start.AddMinutes(1), true, start.AddMinutes(1)) with
        { LastActivityEventAtUtc = start.AddMinutes(1) };
        var codex = Candidate(ProviderIds.Codex, start, true, start) with
        { LastActivityEventAtUtc = start };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude, codex], start.AddMinutes(1))?.ProviderId);

        codex = codex with { LastActivityEventAtUtc = start.AddMinutes(2), LastObservedAtUtc = start.AddMinutes(2) };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [claude, codex], start.AddMinutes(2))?.ProviderId);

        // Polling or a party update must not reclaim the display without a main-agent event.
        claude = claude with { LastObservedAtUtc = start.AddMinutes(3) };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [claude, codex], start.AddMinutes(3))?.ProviderId);
        claude = claude with { LastActivityEventAtUtc = start.AddMinutes(4) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude, codex], start.AddMinutes(4))?.ProviderId);
    }

    [Fact]
    public void Select_ClaudeActive_DoesNotFollowNewerIdleCodexObservation()
    {
        var now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.ClaudeCode);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true, now) with { LastActivityEventAtUtc = now };
        var codex = Candidate(ProviderIds.Codex, now.AddMinutes(1), false) with
        { LastActivityEventAtUtc = now.AddMinutes(1) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude, codex], now.AddMinutes(1))?.ProviderId);
    }

    [Fact]
    public void Select_ClaudeBecomesIdle_FollowsNewCodexEventFromAnOlderTask()
    {
        var now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.ClaudeCode);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true, now) with { LastActivityEventAtUtc = now };
        var codex = Candidate(ProviderIds.Codex, now.AddMinutes(-1), true, now.AddMinutes(-1)) with
        { LastActivityEventAtUtc = now.AddMinutes(-1) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude, codex], now)?.ProviderId);
        claude = claude with { IsActive = false, LastObservedAtUtc = now.AddSeconds(3) };
        codex = codex with { LastActivityEventAtUtc = now.AddSeconds(6) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude, codex], now.AddSeconds(6))?.ProviderId);
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [claude, codex], now.AddSeconds(15))?.ProviderId);
    }

    [Fact]
    public void Select_RapidAlternation_WaitsFifteenSecondsAndUsesLatestCandidate()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(-1), true);
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now)?.ProviderId);
        claude = claude with { LastObservedAtUtc = now.AddSeconds(3) };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(3))?.ProviderId);
        codex = codex with { LastObservedAtUtc = now.AddSeconds(12) };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(15))?.ProviderId);
        claude = claude with { LastObservedAtUtc = now.AddSeconds(18) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(18))?.ProviderId);
        codex = codex with { LastObservedAtUtc = now.AddSeconds(21) };
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(32.999))?.ProviderId);
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(33))?.ProviderId);
    }

    [Fact]
    public void Select_PendingProviderBecomesUnavailable_DoesNotSwitchToCachedCandidate()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(3), true);
        SelectAndAcknowledge(gate, [codex], now);
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(3))?.ProviderId);
        claude = claude with { IsAvailable = false };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(15))?.ProviderId);
    }

    [Fact]
    public void Reset_ProjectChange_PreservesMinimumSwitchInterval()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(3), true);
        SelectAndAcknowledge(gate, [codex], now);
        gate.Reset(ProviderIds.Codex);
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(3))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [codex, claude], now.AddSeconds(15))?.ProviderId);
    }

    [Fact]
    public void Select_CurrentProviderMissing_ClearsDuringCooldownThenUsesFreshReplacement()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        SelectAndAcknowledge(gate, [Candidate(ProviderIds.Codex, now, true)], now);
        Assert.Null(SelectAndAcknowledge(gate, [Candidate(ProviderIds.ClaudeCode, now.AddSeconds(3), true)], now.AddSeconds(3)));
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(6), true);
        Assert.Null(SelectAndAcknowledge(gate, [claude], now.AddSeconds(6)));
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [claude], now.AddSeconds(15))?.ProviderId);
    }

    [Theory]
    [InlineData(ProviderIds.Codex, ProviderIds.ClaudeCode, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Antigravity, ProviderIds.Codex)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.Codex, ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.Codex, ProviderIds.Antigravity, ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Codex, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.ClaudeCode, ProviderIds.Codex)]
    public void Select_AllCliPairsHoldOwnerForFifteenSecondsAfterEachSwitch(
        string initial, string next, string third)
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(initial);
        var current = Candidate(initial, now, true);
        SelectAndAcknowledge(gate, [current], now);
        var replacement = Candidate(next, now, true) with { LastActivityEventAtUtc = now.AddSeconds(3) };
        for (var second = 1; second < 15; second++)
        {
            replacement = replacement with { LastActivityEventAtUtc = now.AddSeconds(second) };
            Assert.Equal(initial, SelectAndAcknowledge(gate, [current, replacement], now.AddSeconds(second))?.ProviderId);
        }
        Assert.Equal(initial, SelectAndAcknowledge(gate, [current, replacement], now.AddSeconds(14.999))?.ProviderId);
        Assert.Equal(next, SelectAndAcknowledge(gate, [current, replacement], now.AddSeconds(15))?.ProviderId);
        var newest = Candidate(third, now, true) with { LastActivityEventAtUtc = now.AddSeconds(18) };
        Assert.Equal(next, SelectAndAcknowledge(gate, [current, replacement, newest], now.AddSeconds(29.999))?.ProviderId);
        Assert.Equal(third, SelectAndAcknowledge(gate, [current, replacement, newest], now.AddSeconds(30))?.ProviderId);
    }

    [Fact]
    public void Select_SameCliActivityUpdatesImmediatelyWithoutExtendingSwitchCooldown()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        SelectAndAcknowledge(gate, [codex], now);
        var updated = codex with { LastActivityEventAtUtc = now.AddSeconds(12) };
        Assert.Same(updated, SelectAndAcknowledge(gate, [updated], now.AddSeconds(12)));
        var claude = Candidate(ProviderIds.ClaudeCode, now, true) with { LastActivityEventAtUtc = now.AddSeconds(13.5) };
        Assert.Equal(ProviderIds.Codex, SelectAndAcknowledge(gate, [updated, claude], now.AddSeconds(14.999))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, SelectAndAcknowledge(gate, [updated, claude], now.AddSeconds(15))?.ProviderId);
    }

    [Theory]
    [InlineData(ProviderIds.Codex, ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.Codex)]
    [InlineData(ProviderIds.Codex, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Codex)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.ClaudeCode)]
    public void Select_DelayedAcknowledgmentHoldsProviderForFifteenSecondsAfterConfirmation(string current, string next)
    {
        var now = DateTimeOffset.Parse("2026-10-09T06:06:59Z");
        var gate = new ProviderActivationGate(current);
        var owner = Candidate(current, now, true);
        var replacement = Candidate(next, now, true) with { LastActivityEventAtUtc = now.AddSeconds(3) };
        gate.Select([owner], now);
        Assert.Equal(current, gate.Select([owner, replacement], now.AddSeconds(15))?.ProviderId);
        var acknowledged = Snapshot(current, now.AddSeconds(15.411));
        Assert.True(gate.RecordPresenceAcknowledgment(acknowledged.PublishedPresence, now.AddSeconds(15.411)));
        Assert.Equal(current, gate.Select([owner, replacement], now.AddSeconds(16.125))?.ProviderId);
        Assert.Equal(current, gate.Select([owner, replacement], now.AddSeconds(30.410))?.ProviderId);
        Assert.Equal(next, gate.Select([owner, replacement], now.AddSeconds(30.411))?.ProviderId);
    }

    [Fact]
    public void Select_ConfirmationTimeoutAllowsFreshReplacementWithoutWaitingForever()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true) with { LastActivityEventAtUtc = now.AddSeconds(3) };
        gate.Select([codex], now);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(29.999))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.Add(DiscordPresenceClient.ResponseTimeout))?.ProviderId);
    }

    [Fact]
    public void Select_SameConnectionActivityAcknowledgmentsDoNotExtendHold()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        gate.Select([codex], now);
        Assert.True(gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now.AddSeconds(9))));
        Assert.False(gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now.AddSeconds(21))));
        var updated = codex with { LastActivityEventAtUtc = now.AddSeconds(21) };
        Assert.Same(updated, gate.Select([updated], now.AddSeconds(21)));
        var claude = Candidate(ProviderIds.ClaudeCode, now, true) with { LastActivityEventAtUtc = now.AddSeconds(22.5) };
        Assert.Equal(ProviderIds.Codex, gate.Select([updated, claude], now.AddSeconds(23.999))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([updated, claude], now.AddSeconds(24))?.ProviderId);
    }

    [Fact]
    public void Select_ReconnectedProviderHoldsAgainAfterItsFirstNewConnectionAcknowledgment()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        gate.Select([codex], now);
        gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now));
        var reconnected = Snapshot(ProviderIds.Codex, now.AddSeconds(27)) with
        {
            PublishedPresence = Snapshot(ProviderIds.Codex, now.AddSeconds(27)).PublishedPresence! with { PublicationGeneration = 2 }
        };
        Assert.True(gate.RecordPresenceAcknowledgment(reconnected.PublishedPresence, now.AddSeconds(27)));
        var claude = Candidate(ProviderIds.ClaudeCode, now, true) with { LastActivityEventAtUtc = now.AddSeconds(30) };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(41.999))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.AddSeconds(42))?.ProviderId);
    }

    [Fact]
    public void RecordPublication_RejectsOtherProviderStaleFutureAndUnacknowledgedResponses()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        gate.Select([Candidate(ProviderIds.Codex, now, true)], now);
        Assert.False(gate.RecordDashboardPublication(Snapshot(ProviderIds.ClaudeCode, now.AddSeconds(6))));
        Assert.False(gate.RecordPresenceAcknowledgment(Snapshot(ProviderIds.Codex, now.AddSeconds(-1)).PublishedPresence, now));
        Assert.False(gate.RecordPresenceAcknowledgment(Snapshot(ProviderIds.Codex, now.AddSeconds(3)).PublishedPresence, now));
        Assert.False(gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now) with { HasNoActiveProvider = true }));
        Assert.False(gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now) with { PublishedPresence = null }));
        Assert.True(gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now.AddSeconds(6))));
    }

    [Fact]
    public void Select_DelayedDashboardPublicationAndProjectResetPreserveFifteenSecondHold()
    {
        var now = DateTimeOffset.Parse("2026-10-09T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        gate.Select([codex], now);
        gate.RecordPresenceAcknowledgment(Snapshot(ProviderIds.Codex, now.AddSeconds(3)).PublishedPresence, now.AddSeconds(3));
        gate.RecordDashboardPublication(Snapshot(ProviderIds.Codex, now.AddSeconds(21)));
        gate.Reset(ProviderIds.Codex);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true) with { LastActivityEventAtUtc = now.AddSeconds(24) };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(35.999))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.AddSeconds(36))?.ProviderId);
    }

    // Existing selection-policy scenarios explicitly assume immediate delivery.
    private static ProviderSelectionCandidate? SelectAndAcknowledge(
        ProviderActivationGate gate, IEnumerable<ProviderSelectionCandidate> candidates, DateTimeOffset nowUtc)
    {
        var selected = gate.Select(candidates, nowUtc);
        if (selected is not null)
        {
            gate.RecordDashboardPublication(Snapshot(selected.ProviderId, nowUtc));
        }
        return selected;
    }

    private static PresenceDashboardSnapshot Snapshot(string providerId, DateTimeOffset nowUtc) =>
        PresenceDashboardSnapshot.Empty with
        {
            ProviderId = providerId,
            UpdatedAtUtc = nowUtc.UtcDateTime,
            Presence = new("details", "state", null, "", [], null, CodexActivityKind.Ready, RunningCommandKind.Unknown, ""),
            PublishedPresence = new("details", "state", null, null, null, null, null, null, null, [])
            {
                ProviderId = providerId,
                AcknowledgedAtUtc = nowUtc.UtcDateTime,
                PublicationGeneration = 1
            }
        };

    private static ProviderSelectionCandidate Candidate(
        string providerId,
        DateTimeOffset observedAt,
        bool isActive,
        DateTimeOffset? activityStartedAt = null)
    {
        return new ProviderSelectionCandidate(
            providerId,
            IsEnabled: true,
            IsAvailable: true,
            LastObservedAtUtc: observedAt,
            HasProjectPath: false,
            IsProjectMatch: false,
            IsActive: isActive,
            ActivityStartedAtUtc: activityStartedAt);
    }
}
