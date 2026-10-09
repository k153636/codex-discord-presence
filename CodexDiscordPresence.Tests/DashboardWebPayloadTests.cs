using System.Security.Cryptography;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardWebPayloadTests
{
    [Fact]
    public void OriginalDashboard_RemainsByteIdenticalToReceivedExport()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Dashboard", "Dashboard.html");
        Assert.Equal("587933D7CFF0CCE6A4D148BFE4038CCC0EBBB9339AC2474E5F4EE88DBB201D16",
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))));
        Assert.Contains("<template id=\"dc-Dashboard\">", File.ReadAllText(path));
    }

    [Fact]
    public void Create_UsesAcknowledgedActivityAndChildTooltipRatherThanDraft()
    {
        var snapshot = PresenceDashboardSnapshot.Empty with
        {
            ProviderId = ProviderIds.ClaudeCode,
            Presence = new("draft", "draft activity", null, "draft tooltip", [], null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, ""),
            PublishedPresence = new("ack", "Reading main.cs", null, null, "rpc_coding", "1 subagent · editing", null, 2, 2, [])
        };
        var payload = DashboardWebPayload.Create(snapshot, new PresenceRuntimeState(), DateTime.UtcNow);
        Assert.Equal("ack", payload.Details);
        Assert.Equal("Reading main.cs", payload.Activity);
        Assert.Equal("1 subagent · editing", payload.SmallText);
        Assert.NotNull(payload.SmallImage);
        Assert.Equal("Claude Code", payload.OwnerName);
        Assert.Empty(payload.Metrics);
    }

    [Fact]
    public void Create_SoloOrUnacknowledgedPresenceDoesNotInventSmallImage()
    {
        var payload = DashboardWebPayload.Create(PresenceDashboardSnapshot.Empty, new PresenceRuntimeState(), DateTime.UtcNow);
        Assert.False(payload.HasPublishedPresence);
        Assert.Null(payload.Provider);
        Assert.Null(payload.SmallImage);
        Assert.Null(payload.SmallText);
        Assert.Equal("No active provider", payload.OwnerName);
    }

    [Fact]
    public void LocalImageReference_ReducedMotionUsesCachedStaticFrameWithoutEditingGif()
    {
        var first = DashboardWebPayload.LocalImageReference("rpc_reading", animationsEnabled: false);
        Assert.StartsWith("data:image/png;base64,", first);
        Assert.Same(first, DashboardWebPayload.LocalImageReference("rpc_reading", animationsEnabled: false));
        Assert.EndsWith("rpc_reading.gif", DashboardWebPayload.LocalImageReference("rpc_reading", animationsEnabled: true));
    }

    [Theory]
    [InlineData("claude_idle", "clawd-sleeping.gif")]
    [InlineData("claude_notification", "clawd-notification.gif")]
    [InlineData("clawd-working-typing", "clawd-working-typing.gif")]
    public void LocalImageReference_ClaudeInternalFallbackUsesItsOwnSourceArt(string key, string filename) =>
        Assert.Equal("https://rpc-art.local/ClaudeCode/" + filename,
            DashboardWebPayload.LocalImageReference(key, animationsEnabled: true));

    [Fact]
    public void Create_DisabledAndNoActiveProviderOmitUsageAndOwner()
    {
        var runtime = new PresenceRuntimeState {Enabled=false};
        var payload = DashboardWebPayload.Create(PresenceDashboardSnapshot.Empty with
        {
            ProviderId=ProviderIds.Codex,
            TokenUsage=new(null,null,"subsc",new(25,300,DateTime.UtcNow))
        }, runtime, DateTime.UtcNow);
        Assert.Null(payload.Provider);
        Assert.Empty(payload.Metrics);
        Assert.Equal("No current provider activity.", payload.UsageNote);
    }

    [Theory]
    [InlineData("file:///C:/private/file.png")]
    [InlineData("C:\\private\\file.png")]
    [InlineData("javascript:alert(1)")]
    [InlineData("../private.png")]
    public void LocalImageReference_DoesNotExposePrivatePathsOrUnsafeSchemes(string reference) =>
        Assert.Null(DashboardWebPayload.LocalImageReference(reference));
}
