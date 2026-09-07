using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexDashboardFormTests
{
    [Fact]
    public void Form_UsesSingleOverviewPage_WithCompactDesktopBaseline()
    {
        Size? windowSize = null;
        Size? minimumSize = null;
        var rootControlCount = -1;
        TableLayoutPanel? layout = null;
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
                preview = overviewLayout.GetControlFromPosition(2, 0);
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
        Assert.Equal(new Size(880, 420), windowSize);
        Assert.Equal(new Size(880, 420), minimumSize);
        Assert.Equal(1, rootControlCount);
        Assert.NotNull(layout);
        Assert.Equal(3, layout!.ColumnCount);
        Assert.IsType<DashboardPreviewSurface>(preview);
    }

    [Fact]
    public void Dashboard_UsesEnglishDiscordUiLabels()
    {
        Assert.Equal("Current Activity", DashboardLabels.CurrentActivity);
        Assert.Equal("Playing:", DashboardLabels.Playing);
    }
}
