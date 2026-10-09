using System.Drawing;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardResourceTests
{
    [Fact]
    public void ImageSlot_ChangedRemoteReferenceImmediatelyDropsPreviousProviderImage()
    {
        using var fallback = new Bitmap(2, 2);
        using var slot = new DashboardPresenceImageSlot(fallback, () => { });
        slot.SetReference("rpc_codex");
        Assert.NotSame(fallback, slot.CurrentImage);
        slot.SetReference("https://example.invalid/next-provider.png");
        Assert.Same(fallback, slot.CurrentImage);
    }
}
