using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexDashboardFormTests
{
    [Fact]
    public void Form_UsesCompactPortraitSingleColumnOverview_WithPreviewBelowExistingInformation()
    {
        Size? windowSize = null;
        Size? minimumSize = null;
        var rootControlCount = -1;
        TableLayoutPanel? layout = null;
        Control? overview = null;
        Control? preview = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new CodexDashboardForm(new PresenceRuntimeState());
                Assert.Equal(ProductBrand.Name, form.Text);
                Assert.Equal($"{ProductBrand.Name} dashboard", form.AccessibleName);
                windowSize = form.Size;
                minimumSize = form.MinimumSize;
                rootControlCount = form.Controls.Count;
                var overviewLayout = form.Controls.OfType<TableLayoutPanel>().Single();
                layout = overviewLayout;
                overview = overviewLayout.GetControlFromPosition(0, 1);
                preview = overviewLayout.GetControlFromPosition(0, 2);
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(failure);
        Assert.InRange(windowSize!.Value.Width, 400, 410);
        Assert.InRange(windowSize.Value.Height, 378, 390);
        Assert.Equal(windowSize, minimumSize);
        Assert.Equal(1, rootControlCount);
        Assert.NotNull(layout);
        Assert.Equal(1, layout!.ColumnCount);
        Assert.Equal(3, layout.RowCount);
        Assert.Equal(new Padding(12), layout.Padding);
        Assert.IsType<DashboardOverviewSurface>(overview);
        Assert.IsType<DashboardPreviewSurface>(preview);
        Assert.Equal(Padding.Empty, preview!.Margin);
    }

    [Fact]
    public void Form_ExposesAccessibleProviderCheckBoxes_AndPersistsChanges()
    {
        var statePath = Path.Combine(
            Path.GetTempPath(),
            "codex-dashboard-" + Guid.NewGuid().ToString("N"),
            "presence-state.json");
        try
        {
            var state = new PresenceRuntimeState();
            state.InitializeProviderEnabled(new Dictionary<string, bool>
            {
                [ProviderIds.Codex] = true,
                [ProviderIds.Antigravity] = false
            });

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    using var form = new CodexDashboardForm(state, new PresenceStateStore(), statePath);
                    var panel = form.Controls
                        .OfType<TableLayoutPanel>()
                        .Single()
                        .GetControlFromPosition(0, 0) as ProviderIntegrationPanel;

                    Assert.NotNull(panel);
                    Assert.True(panel!.CodexCheckBox.Checked);
                    Assert.False(panel.AntigravityCheckBox.Checked);
                    Assert.Equal("Codex integration", panel.CodexCheckBox.AccessibleName);
                    Assert.False(string.IsNullOrWhiteSpace(panel.CodexCheckBox.AccessibleDescription));
                    Assert.Equal("Antigravity integration", panel.AntigravityCheckBox.AccessibleName);
                    Assert.False(string.IsNullOrWhiteSpace(panel.AntigravityCheckBox.AccessibleDescription));

                    panel.AntigravityCheckBox.Checked = true;
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.Null(failure);
            Assert.True(state.IsProviderEnabled(ProviderIds.Antigravity, false));
            var loaded = new PresenceStateStore().Load(statePath);
            Assert.True(loaded.IsProviderEnabled(ProviderIds.Antigravity, false));
        }
        finally
        {
            var directory = Path.GetDirectoryName(statePath);
            if (directory is not null && Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public void Dashboard_DerivesDiscordActivityTypeLabelFromPublishedPayload()
    {
        var presence = new DiscordPresenceSnapshot(
            "details",
            "state",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            [])
        {
            ActivityType = DiscordRPC.ActivityType.Watching
        };

        Assert.Equal("Watching:", DashboardTextFormatter.FormatActivityType(presence));
    }

    [Theory]
    [InlineData(364, 2, 360)]
    [InlineData(380, 10, 360)]
    [InlineData(464, 12, 440)]
    public void DashboardLayoutMetrics_AlignsOverviewAndPreviewLeftEdge(
        int clientWidth,
        int expectedLeft,
        int expectedContentWidth)
    {
        Assert.Equal(expectedLeft, DashboardLayoutMetrics.GetContentLeft(clientWidth));
        Assert.Equal(expectedContentWidth, DashboardLayoutMetrics.GetContentWidth(clientWidth));
    }
}
