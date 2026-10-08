using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class ProviderActivationGateTests
{
    [Fact]
    public void Select_UsesActivityStartToAvoidSwitchingBackToAnOlderActiveProvider()
    {
        var initialTime = DateTimeOffset.Parse("2026-09-12T01:35:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Antigravity);

        Assert.Equal(
            ProviderIds.Antigravity,
            gate.Select(
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
            gate.Select(
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
            gate.Select(
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
        var selected = gate.Select(
            [
                Candidate(ProviderIds.Antigravity, observedAt: initialTime, isActive: true, activityStartedAt: initialTime),
                Candidate(ProviderIds.Codex, observedAt: initialTime, isActive: false)
            ],
            initialTime);
        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);

        // 2. Antigravity transitions to idle at 21:40:09
        var idleTime = DateTimeOffset.Parse("2026-09-12T21:40:09Z");
        selected = gate.Select(
            [
                Candidate(ProviderIds.Antigravity, observedAt: idleTime, isActive: false),
                Candidate(ProviderIds.Codex, observedAt: idleTime, isActive: false)
            ],
            idleTime);
        Assert.Equal(ProviderIds.Antigravity, selected?.ProviderId);

        // 3. Codex starts thinking at 21:45:13
        var codexStartTime = DateTimeOffset.Parse("2026-09-12T21:45:13Z");
        selected = gate.Select(
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
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude, codex], start.AddMinutes(1))?.ProviderId);

        codex = codex with { LastActivityEventAtUtc = start.AddMinutes(2), LastObservedAtUtc = start.AddMinutes(2) };
        Assert.Equal(ProviderIds.Codex, gate.Select([claude, codex], start.AddMinutes(2))?.ProviderId);

        // Polling or a party update must not reclaim the display without a main-agent event.
        claude = claude with { LastObservedAtUtc = start.AddMinutes(3) };
        Assert.Equal(ProviderIds.Codex, gate.Select([claude, codex], start.AddMinutes(3))?.ProviderId);
        claude = claude with { LastActivityEventAtUtc = start.AddMinutes(4) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude, codex], start.AddMinutes(4))?.ProviderId);
    }

    [Fact]
    public void Select_ClaudeActive_DoesNotFollowNewerIdleCodexObservation()
    {
        var now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.ClaudeCode);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true, now) with { LastActivityEventAtUtc = now };
        var codex = Candidate(ProviderIds.Codex, now.AddMinutes(1), false) with
        { LastActivityEventAtUtc = now.AddMinutes(1) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude, codex], now.AddMinutes(1))?.ProviderId);
    }

    [Fact]
    public void Select_ClaudeBecomesIdle_FollowsNewCodexEventFromAnOlderTask()
    {
        var now = DateTimeOffset.Parse("2026-10-07T10:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.ClaudeCode);
        var claude = Candidate(ProviderIds.ClaudeCode, now, true, now) with { LastActivityEventAtUtc = now };
        var codex = Candidate(ProviderIds.Codex, now.AddMinutes(-1), true, now.AddMinutes(-1)) with
        { LastActivityEventAtUtc = now.AddMinutes(-1) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude, codex], now)?.ProviderId);
        claude = claude with { IsActive = false, LastObservedAtUtc = now.AddSeconds(1) };
        codex = codex with { LastActivityEventAtUtc = now.AddSeconds(2) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude, codex], now.AddSeconds(2))?.ProviderId);
        Assert.Equal(ProviderIds.Codex, gate.Select([claude, codex], now.AddSeconds(5))?.ProviderId);
    }

    [Fact]
    public void Select_RapidAlternation_WaitsFiveSecondsAndUsesLatestCandidate()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(-1), true);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now)?.ProviderId);
        claude = claude with { LastObservedAtUtc = now.AddSeconds(1) };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(1))?.ProviderId);
        codex = codex with { LastObservedAtUtc = now.AddSeconds(4) };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(5))?.ProviderId);
        claude = claude with { LastObservedAtUtc = now.AddSeconds(6) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.AddSeconds(6))?.ProviderId);
        codex = codex with { LastObservedAtUtc = now.AddSeconds(7) };
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.AddSeconds(10.999))?.ProviderId);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(11))?.ProviderId);
    }

    [Fact]
    public void Select_PendingProviderBecomesUnavailable_DoesNotSwitchToCachedCandidate()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(1), true);
        gate.Select([codex], now);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(1))?.ProviderId);
        claude = claude with { IsAvailable = false };
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(5))?.ProviderId);
    }

    [Fact]
    public void Reset_ProjectChange_PreservesMinimumSwitchInterval()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        var codex = Candidate(ProviderIds.Codex, now, true);
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(1), true);
        gate.Select([codex], now);
        gate.Reset(ProviderIds.Codex);
        Assert.Equal(ProviderIds.Codex, gate.Select([codex, claude], now.AddSeconds(1))?.ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([codex, claude], now.AddSeconds(5))?.ProviderId);
    }

    [Fact]
    public void Select_CurrentProviderMissing_ClearsDuringCooldownThenUsesFreshReplacement()
    {
        var now = DateTimeOffset.Parse("2026-10-08T00:00:00Z");
        var gate = new ProviderActivationGate(ProviderIds.Codex);
        gate.Select([Candidate(ProviderIds.Codex, now, true)], now);
        Assert.Null(gate.Select([Candidate(ProviderIds.ClaudeCode, now.AddSeconds(1), true)], now.AddSeconds(1)));
        var claude = Candidate(ProviderIds.ClaudeCode, now.AddSeconds(2), true);
        Assert.Null(gate.Select([claude], now.AddSeconds(2)));
        Assert.Equal(ProviderIds.ClaudeCode, gate.Select([claude], now.AddSeconds(5))?.ProviderId);
    }

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
