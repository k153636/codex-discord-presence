using System.Drawing;
using System.Runtime.ExceptionServices;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardSubagentPreviewTests
{
    [Fact]
    public void Preview_SubagentBadge_UsesAcknowledgedTooltipAndAccessibleStatus()
    {
        RunSta(() =>
        {
            using var preview = new DashboardPreviewSurface();
            var snapshot = PresenceDashboardSnapshot.Empty with
            {
                ProviderId = ProviderIds.Codex,
                PublishedPresence = new("details", "main activity", "rpc_reading", null,
                    "rpc_coding", "2 subagents · editing", null, 3, 3, [])
            };
            preview.SetSnapshot(snapshot, true);
            var scale = preview.DeviceDpi / 96f;
            var badgePoint = new Point((int)(90 * scale), (int)(148 * scale));

            Assert.Equal("2 subagents · editing", preview.GetSmallImageToolTip(badgePoint));
            Assert.Contains("2 subagents · editing", preview.AccessibleDescription);
            Assert.Contains("main activity", preview.AccessibleDescription);
            Assert.Null(preview.GetSmallImageToolTip(new Point(1, 1)));

            preview.SetSnapshot(snapshot with
            {
                PublishedPresence = snapshot.PublishedPresence with { SmallImageKey = null, SmallImageText = null }
            }, true);
            Assert.Null(preview.GetSmallImageToolTip(badgePoint));
            Assert.DoesNotContain("subagents", preview.AccessibleDescription);
        });
    }

    [Fact]
    public void Preview_WithoutAcknowledgment_DoesNotShowChildStatus()
    {
        RunSta(() =>
        {
            using var preview = new DashboardPreviewSurface();
            preview.SetSnapshot(PresenceDashboardSnapshot.Empty with
            {
                Presence = new("details", "main activity", null, "2 subagents · editing", [], null,
                    CodexActivityKind.ApplyingEdits, RunningCommandKind.Unknown, "")
            }, true);

            Assert.Null(preview.GetSmallImageToolTip(new Point(90, 148)));
            Assert.DoesNotContain("subagents", preview.AccessibleDescription);
        });
    }

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
