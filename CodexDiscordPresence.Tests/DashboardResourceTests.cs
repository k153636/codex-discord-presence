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
    public void ImageSlot_DisposeReleasesOwnedImageAndRejectsFurtherReferences()
    {
        using var fallback = new Bitmap(2, 2);
        using var slot = new DashboardPresenceImageSlot(fallback, () => { });
        slot.SetReference("rpc_codex");
        Assert.NotSame(fallback, slot.CurrentImage);
        slot.Dispose();
        Assert.Same(fallback, slot.CurrentImage);
        slot.SetReference("rpc_antigravity_cli");
        Assert.Same(fallback, slot.CurrentImage);
        slot.Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void Form_DisabledOrIdleProviderCannotRetainLiveChip(bool enabled, bool noActiveProvider)
    {
        RunSta(() =>
        {
            var runtime = new PresenceRuntimeState { Enabled = enabled };
            runtime.PublishDashboardSnapshot(PresenceDashboardSnapshot.Empty with
            {
                ProviderId = ProviderIds.Codex,
                HasNoActiveProvider = noActiveProvider,
                Presence = new("details", "state", null, "", [], null, CodexActivityKind.AnalyzingProject, RunningCommandKind.Unknown, "")
            });
            using var form = new CodexDashboardForm(runtime);
            typeof(CodexDashboardForm).GetMethod("RefreshSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, null);
            var layout = form.Controls.OfType<TableLayoutPanel>().Single();
            var panel = Assert.IsType<ProviderIntegrationPanel>(layout.GetControlFromPosition(0, 0));
            Assert.Equal("Standby", panel.CodexCheckBox.AccessibleDescription);
        });
    }

    [Fact]
    public void Overview_AccessibleDescriptionIncludesUsageAndSuppressesDisabledValues()
    {
        RunSta(() =>
        {
            using var overview = new DashboardOverviewSurface();
            var snapshot = PresenceDashboardSnapshot.Empty with
            {
                ProviderId = ProviderIds.Codex,
                TokenUsage = new(null, null, "subsc", new(25, 300, DateTime.UtcNow.AddHours(1)))
            };
            overview.SetSnapshot(snapshot, true);
            Assert.Contains("Billing subsc", overview.AccessibleDescription);
            Assert.Contains("5h limit 25% used", overview.AccessibleDescription);
            overview.SetSnapshot(snapshot, false);
            Assert.DoesNotContain("25%", overview.AccessibleDescription);
            Assert.Contains("No current provider activity", overview.AccessibleDescription);
        });
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

    [Theory]
    [InlineData(96)]
    [InlineData(120)]
    [InlineData(144)]
    [InlineData(192)]
    public void ProviderNames_FitCompleteInMinimumWidthLayoutAtSimulatedDpi(int dpi)
    {
        RunSta(() =>
        {
            var scale = dpi / 96f;
            using var panel = new ProviderIntegrationPanel { Size = new Size(360, 54) };
            panel.PerformLayout();
            panel.Scale(new SizeF(scale, scale));
            panel.PerformLayout();
            using var bitmap = new Bitmap((int)(360 * scale), (int)(54 * scale));
            using var g = Graphics.FromImage(bitmap);
            using var font = new Font("Segoe UI", 12, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
            var chips = new[] { panel.CodexCheckBox, panel.ClaudeCodeCheckBox, panel.AntigravityCheckBox };
            Assert.Equal(["Codex", "Claude Code", "Antigravity CLI"], chips.Select(chip => chip.Text));
            foreach (var chip in chips)
            {
                var textWidth = g.MeasureString(chip.Text, font, int.MaxValue, format).Width;
                var availableWidth = chip.Width / scale - 35;
                Assert.True(textWidth <= availableWidth, $"{chip.Text}: {textWidth} > {availableWidth}, DPI {dpi}");
            }
            Assert.Equal([0, 1, 2], chips.Select(chip => chip.TabIndex));
        });
    }

    [Fact]
    public void Preview_UsesOnlyAcknowledgedPresenceAndNoSpeculativeButtonSpace()
    {
        RunSta(() =>
        {
            using var preview = new DashboardPreviewSurface();
            preview.SetSnapshot(PresenceDashboardSnapshot.Empty with { IsDiscordConnected = true }, true);
            Assert.Contains("Waiting for Discord", preview.AccessibleDescription);
            Assert.Equal(178, DashboardPreviewSurface.GetPreferredHeight(PresenceDashboardSnapshot.Empty));
            preview.SetSnapshot(PresenceDashboardSnapshot.Empty, false);
            Assert.Contains("Presence disabled", preview.AccessibleDescription);
            preview.SetSnapshot(PresenceDashboardSnapshot.Empty with { HasNoActiveProvider = true }, true);
            Assert.Contains("No active coding client", preview.AccessibleDescription);
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
