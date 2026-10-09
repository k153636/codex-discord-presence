namespace CodexDiscordPresence.Tests;

public sealed class DashboardSnapshotSelectorTests
{
    private static readonly DateTime Start = new(2026, 10, 9, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(ProviderIds.Codex, ProviderIds.ClaudeCode)]
    [InlineData(ProviderIds.ClaudeCode, ProviderIds.Antigravity)]
    [InlineData(ProviderIds.Antigravity, ProviderIds.Codex)]
    public void Select_DelayedFirstPreviewPaintHoldsWholeProviderForFiveSeconds(string owner, string next)
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(owner);
        selector.Select(first with { PublishedPresence = null }, Start);
        selector.RecordOwnerDisplayed(first, Start.AddSeconds(0.5));
        selector.Select(first, Start.AddSeconds(5.4));
        selector.RecordPresenceDisplayed(first, Start.AddSeconds(5.4));
        Assert.Equal(owner, selector.Select(Snapshot(next), Start.AddSeconds(5.5)).ProviderId);
        Assert.Equal(owner, selector.Select(Snapshot(next), Start.AddSeconds(10.399)).ProviderId);
        Assert.Equal(next, selector.Select(Snapshot(next), Start.AddSeconds(10.4)).ProviderId);
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
        Assert.Same(updated, selector.Select(updated, Start.AddSeconds(4)));
        selector.RecordOwnerDisplayed(updated, Start.AddSeconds(4));
        selector.RecordPresenceDisplayed(updated, Start.AddSeconds(4));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(5)).ProviderId);
    }

    [Fact]
    public void Select_ReevaluatesNewestSnapshotRatherThanReplayingPendingSwitch()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordOwnerDisplayed(first, Start);
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(1)).ProviderId);
        Assert.Equal(ProviderIds.Antigravity, selector.Select(Snapshot(ProviderIds.Antigravity), Start.AddSeconds(5)).ProviderId);
    }

    [Fact]
    public void Select_HeldCardRetainsItsProviderAndPayloadWhileConnectionStatusStaysCurrent()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        var connecting = Snapshot(ProviderIds.ClaudeCode) with { IsDiscordConnected = false, IsDiscordConnecting = true };
        var held = selector.Select(connecting, Start.AddSeconds(1));
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
        Assert.Same(idle, selector.Select(idle, Start.AddSeconds(1)));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(2)).ProviderId);
        selector.Reset();
        Assert.Equal(ProviderIds.Antigravity, selector.Select(Snapshot(ProviderIds.Antigravity), Start.AddSeconds(2.1)).ProviderId);
    }

    [Fact]
    public void Select_PaintAfterLongUiDelayStartsHoldAtActualDrawingTime()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordOwnerDisplayed(first, Start.AddSeconds(9));
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(9.1)).ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(14)).ProviderId);
    }

    [Fact]
    public void RecordPresenceDisplayed_AnotherProvidersResponseCannotExtendCurrentHold()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordPresenceDisplayed(first with { PublishedPresence = Snapshot(ProviderIds.ClaudeCode).PublishedPresence }, Start.AddSeconds(4));
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(5)).ProviderId);
    }

    [Fact]
    public void Select_FirstPaintAfterReconnectionRestartsHoldButSubsequentFramesDoNot()
    {
        var selector = new DashboardSnapshotSelector();
        var first = Snapshot(ProviderIds.Codex);
        selector.Select(first, Start);
        selector.RecordPresenceDisplayed(first, Start);
        var reconnected = first with { PublishedPresence = first.PublishedPresence! with { PublicationGeneration = 2 } };
        selector.Select(reconnected, Start.AddSeconds(10));
        selector.RecordPresenceDisplayed(reconnected, Start.AddSeconds(10));
        selector.RecordPresenceDisplayed(reconnected, Start.AddSeconds(14));
        Assert.Equal(ProviderIds.Codex, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(14.999)).ProviderId);
        Assert.Equal(ProviderIds.ClaudeCode, selector.Select(Snapshot(ProviderIds.ClaudeCode), Start.AddSeconds(15)).ProviderId);
    }

    private static PresenceDashboardSnapshot Snapshot(string providerId) => PresenceDashboardSnapshot.Empty with
    {
        ProviderId = providerId,
        IsDiscordConnected = true,
        Presence = new("details", "state", null, "", [], null, CodexActivityKind.Ready, RunningCommandKind.Unknown, ""),
        PublishedPresence = new("details", "state", null, null, null, null, null, null, null, []) { ProviderId = providerId }
    };
}
