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
