using System.Runtime.ExceptionServices;
using System.Windows.Forms;

namespace CodexDiscordPresence.Tests;

public sealed class ApplicationUpdateMenuTests
{
    [Fact]
    public void Menu_OffersPersistentAutomaticPreferenceAndManualControls()
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "rpc-menu-tests-" + Guid.NewGuid());
            try
            {
                var store = new UpdatePreferencesStore(Path.Combine(directory, "preferences.json"));
                var updates = new ApplicationUpdateCoordinator(new Backend(), store, () => false,
                    () => { }, () => [], () => { }, _ => { });
                using var icon = new NotifyIcon();
                using var menu = new ApplicationUpdateMenu(updates, icon);
                var items = menu.Menu.DropDownItems.OfType<ToolStripMenuItem>().ToArray();
                Assert.Contains(items, item => item.Text == "Check for updates");
                Assert.Contains(items, item => item.Text == "Release notes / download");
                Assert.False(items.Single(item => item.Text == "Update and restart now").Enabled);
                var automatic = items.Single(item => item.Text == "Automatic updates");
                Assert.True(automatic.Checked);
                automatic.PerformClick();
                Assert.False(updates.Snapshot.AutomaticEnabled);
                Assert.False(store.Load().AutomaticEnabled);
            }
            finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
        });
    }

    [Fact]
    public void CountdownAndPostponement_AreReadableWithoutNegativeSeconds()
    {
        var now = DateTime.UtcNow;
        var ready = new ApplicationUpdateSnapshot(ApplicationUpdateStatus.Ready, "0.2.5", "0.5.0", true, true,
            RestartAtUtc: now.AddSeconds(30));
        Assert.Equal("Restart in 30 seconds", ApplicationUpdateMenu.Describe(ready, now));
        Assert.Equal("Restart in 0 seconds", ApplicationUpdateMenu.Describe(ready, now.AddMinutes(1)));
        Assert.StartsWith("Postponed until", ApplicationUpdateMenu.Describe(ready with { DeferredUntilUtc = now.AddDays(1) }, now));
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

    private sealed class Backend : IApplicationUpdateBackend
    {
        public bool IsInstalled => true;
        public string? PreparedVersion => null;
        public Task<string?> CheckAsync(CancellationToken token) => Task.FromResult<string?>(null);
        public Task DownloadAsync(Action<int> progress, CancellationToken token) => Task.CompletedTask;
        public void ScheduleRestart(string[] arguments) { }
    }
}
