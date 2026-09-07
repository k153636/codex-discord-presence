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
}
