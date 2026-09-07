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
                windowSize = form.Size;
                minimumSize = form.MinimumSize;
                rootControlCount = form.Controls.Count;
                var overviewLayout = form.Controls.OfType<TableLayoutPanel>().Single();
                layout = overviewLayout;
                overview = overviewLayout.GetControlFromPosition(0, 0);
                preview = overviewLayout.GetControlFromPosition(0, 1);
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
        Assert.Equal(new Size(400, 660), windowSize);
        Assert.Equal(new Size(400, 660), minimumSize);
        Assert.Equal(1, rootControlCount);
        Assert.NotNull(layout);
        Assert.Equal(1, layout!.ColumnCount);
        Assert.Equal(2, layout.RowCount);
        Assert.Equal(Padding.Empty, layout.Padding);
        Assert.IsType<DashboardOverviewSurface>(overview);
        Assert.IsType<DashboardPreviewSurface>(preview);
        Assert.Equal(Padding.Empty, preview!.Margin);
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
    [InlineData(464, 16, 432)]
    public void DashboardLayoutMetrics_AlignsOverviewAndPreviewLeftEdge(
        int clientWidth,
        int expectedLeft,
        int expectedContentWidth)
    {
        Assert.Equal(expectedLeft, DashboardLayoutMetrics.GetContentLeft(clientWidth));
        Assert.Equal(expectedContentWidth, DashboardLayoutMetrics.GetContentWidth(clientWidth));
    }
}
