using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

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

    [Fact]
    public void Form_DisposedWithoutShowingStopsRefreshTimer()
    {
        RunSta(() =>
        {
            var form = new CodexDashboardForm(new PresenceRuntimeState());
            var field = typeof(CodexDashboardForm).GetField("_refreshTimer", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var timer = Assert.IsType<System.Windows.Forms.Timer>(field.GetValue(form));
            Assert.True(timer.Enabled);
            form.Dispose();
            form.Dispose();
            Assert.False(timer.Enabled);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
