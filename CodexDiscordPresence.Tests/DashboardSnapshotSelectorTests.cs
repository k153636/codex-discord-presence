namespace CodexDiscordPresence.Tests;

public sealed class DashboardSnapshotSelectorTests
{
    private static readonly DateTime Start = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProviderIds.Codex, ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.Codex)]
    [InlineData(ProviderIds.Codex, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Codex)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.ClaudeCode)]
    public void Select_DelayedFirstPreviewPaintHoldsWholeProviderForFifteenSeconds(string owner, string next)
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(owner);
        selector.Select(first with { PublishedPresence = null }, Start);
        selector.RecordOwnerDisplayed(first, Start.AddSeconds(1.5));
        selector.Select(first, Start.AddSeconds(16.2));
        selector.RecordPresenceDisplayed(first, Start.AddSeconds(16.2));
        Assert.Equal(owner, selector.Select(Snapshot(next), Start.AddSeconds(16.5)).ProviderId);
        Assert.Equal(owner, selector.Select(Snapshot(next), Start.AddSeconds(31.199)).ProviderId);
        Assert.Equal(next, selector.Select(Snapshot(next), Start.AddSeconds(31.2)).ProviderId);
    }

    [Fact]
    public void Select_SameProviderChangesImmediatelyAndRepaintsNeverRestartHold()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordOwnerDisplayed(first, Start);
        selector.RecordPresenceDisplayed(first, Start);
        var updated = first with { ModelName = "changed", PublishedPresence = first.PublishedPresence! with { State = "new activity" } };
        Assert.Same(updated, selector.Select(updated, Start.AddSeconds(12)));
        selector.RecordOwnerDisplayed(updated, Start.AddSeconds(12));
        selector.RecordPresenceDisplayed(updated, Start.AddSeconds(12));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(15)).ProviderId);
    }

    [Fact]
    public void Select_ReevaluatesNewestSnapshotRatherThanReplayingPendingSwitch()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordOwnerDisplayed(first, Start);
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(3)).ProviderId);
        Assert.Equal(ProviderIds.Antigravity, selector.Select(Snapshot(ProviderIds.Antigravity), Start.AddSeconds(15)).ProviderId);
    }

    [Fact]
    public void Select_HeldCardRetainsItsProviderAndPayloadWhileConnectionStatusStaysCurrent()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        var connecting = Snapshot(ProviderIds.ClaudeCode) with { IsDiscordConnected = false, IsDiscordConnecting = true };
        var held = selector.Select(connecting, Start.AddSeconds(3));
        Assert.Equal(ProviderIds.Codex, held.ProviderId);
        Assert.Same(first.PublishedPresence, held.PublishedPresence);
        Assert.False(held.IsDiscordConnected);
        Assert.True(held.IsDiscordConnecting);
    }

    [Fact]
    public void Select_NoActiveProviderAndResetImmediatelyDiscardHeldProvider()
    {
        var selector = new DashboardSnapshotSelector();
        selector.Select(Snapshot(ProviderIds.Codex), Start);
        var idle = PresenceDashboardSnapshot.Empty with { HasNoActiveProvider = true };
        Assert.Same(idle, selector.Select(idle, Start.AddSeconds(3)));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(6)).ProviderId);
        selector.Reset();
        Assert.Equal(ProviderIds.Antigravity, selector.Select(Snapshot(ProviderIds.Antigravity), Start.AddSeconds(6.3)).ProviderId);
    }

    [Fact]
    public void Select_PaintAfterLongUiDelayStartsHoldAtActualDrawingTime()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordOwnerDisplayed(first, Start.AddSeconds(27));
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(27.3)).ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(42)).ProviderId);
    }

    [Fact]
    public void RecordPresenceDisplayed_AnotherProvidersResponseCannotExtendCurrentHold()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordPresenceDisplayed(first with { PublishedPresence = Snapshot(ProviderIds.ClaudeCode).PublishedPresence }, Start.AddSeconds(12));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(15)).ProviderId);
    }

    [Fact]
    public void Select_FirstPaintAfterReconnectionRestartsHoldButSubsequentFramesDoNot()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordPresenceDisplayed(first, Start);
        var reconnected = first with { PublishedPresence = first.PublishedPresence! with { PublicationGeneration = 2 } };
        selector.Select(reconnected, Start.AddSeconds(30));
        selector.RecordPresenceDisplayed(reconnected, Start.AddSeconds(30));
        selector.RecordPresenceDisplayed(reconnected, Start.AddSeconds(42));
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(44.999)).ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(45)).ProviderId);
    }

    private static PresenceDashboardSnapshot Snapshot(string providerId) => PresenceDashboardSnapshot.Empty with
    {
        ProviderId = providerId,
        IsDiscordConnected = true,
        Presence = new("details", "state", null, "", [], null, CodexActivityKind.Ready, RunningCommandKind.Unknown, ""),
        PublishedPresence = new("details", "state", null, null, null, null, null, null, null, []) { ProviderId = providerId }
    };
}
