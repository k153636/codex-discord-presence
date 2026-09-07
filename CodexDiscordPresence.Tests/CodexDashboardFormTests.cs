using System.Drawing;
using System.Threading;
using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class CodexDashboardFormTests
{
    [Fact]
    public void Form_UsesOverviewAndPreviewTabs_WithPreviewSelected()
    {
        string[]? tabNames = null;
        var selectedIndex = -1;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new CodexDashboardForm(new PresenceRuntimeState());
                tabNames = form.TabNames.ToArray();
                selectedIndex = form.SelectedTabIndex;
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
        Assert.NotNull(tabNames);
        Assert.Equal(["Overview", "Preview"], tabNames!);
        Assert.Equal(1, selectedIndex);
    }

    [Fact]
    public void Preview_UsesEnglishDiscordUiLabels()
    {
        Assert.Equal("Current Activity", DashboardPreviewSurface.CurrentActivityLabel);
        Assert.Equal("Playing:", DashboardPreviewSurface.PlayingLabel);
    }

    [Fact]
    public void Form_PreservesDesktopMinimumAndKeyboardNavigationBaseline()
    {
        Size? minimumSize = null;
        bool? keyPreview = null;
        Exception? failure = null;

        var thread = new Thread(() =>
        {
            try
            {
                using var form = new CodexDashboardForm(new PresenceRuntimeState());
                minimumSize = form.MinimumSize;
                keyPreview = form.KeyPreview;
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
        Assert.Equal(new Size(760, 520), minimumSize);
        Assert.True(keyPreview);
    }
}
