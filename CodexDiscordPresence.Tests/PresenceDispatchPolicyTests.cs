using CodexDiscordPresence;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class PresenceDispatchPolicyTests
{
    [Fact]
    public void ShouldSendPresence_WhenSignatureIsUnchangedAndNoRefreshNeeded_ReturnsFalse()
    {
        var shouldSend = PresenceDispatchPolicy.ShouldSendPresence(
            currentSignature: "same",
            lastSentSignature: "same",
            keepAliveDue: false,
            needsRefreshAfterReconnect: false);

        Assert.False(shouldSend);
    }

    [Fact]
    public void ShouldSendPresence_WhenKeepAliveIsDue_ReturnsTrue()
    {
        var shouldSend = PresenceDispatchPolicy.ShouldSendPresence(
            currentSignature: "same",
            lastSentSignature: "same",
            keepAliveDue: true,
            needsRefreshAfterReconnect: false);

        Assert.True(shouldSend);
    }

    [Fact]
    public void ShouldSendPresence_WhenReconnectRequiresRefresh_ReturnsTrue()
    {
        var shouldSend = PresenceDispatchPolicy.ShouldSendPresence(
            currentSignature: "same",
            lastSentSignature: "same",
            keepAliveDue: false,
            needsRefreshAfterReconnect: true);

        Assert.True(shouldSend);
    }

    [Fact]
    public void ShouldSendPresence_WhenSignatureChanges_ReturnsTrue()
    {
        var shouldSend = PresenceDispatchPolicy.ShouldSendPresence(
            currentSignature: "new",
            lastSentSignature: "old",
            keepAliveDue: false,
            needsRefreshAfterReconnect: false);

        Assert.True(shouldSend);
    }
}
