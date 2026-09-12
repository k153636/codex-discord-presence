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
