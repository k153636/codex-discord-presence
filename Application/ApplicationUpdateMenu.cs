using System.Diagnostics;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal sealed class ApplicationUpdateMenu : IDisposable
{
    private readonly ApplicationUpdateCoordinator _updates;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _status = new() { Enabled = false };
    private readonly ToolStripMenuItem _check = new("Check for updates");
    private readonly ToolStripMenuItem _apply = new("Update and restart now");
    private readonly ToolStripMenuItem _defer = new("Remind me tomorrow");
    private readonly ToolStripMenuItem _automatic = new("Automatic updates");
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 500 };
    private string? _notifiedVersion;
    private bool _failedNotificationShown;

    public ToolStripMenuItem Menu { get; } = new("Updates");

    public ApplicationUpdateMenu(ApplicationUpdateCoordinator updates, NotifyIcon icon)
    {
        _updates = updates;
        _icon = icon;
        Menu.DropDownItems.Add(new ToolStripMenuItem($"Current version: {AppVersion.Current}") { Enabled = false });
        Menu.DropDownItems.Add(_status);
        Menu.DropDownItems.Add(new ToolStripSeparator());
        Menu.DropDownItems.AddRange([_check, _apply, _defer, _automatic]);
        var releases = new ToolStripMenuItem("Release notes / download");
        releases.Click += (_, _) => Perform(() => Process.Start(new ProcessStartInfo
        {
            FileName = VelopackUpdateBackend.ReleasesUrl, UseShellExecute = true
        }));
        Menu.DropDownItems.Add(releases);
        _check.Click += (_, _) => _updates.RequestCheck();
        _apply.Click += (_, _) => _updates.RequestRestart();
        _defer.Click += (_, _) => Perform(() => _updates.DeferUntil(DateTime.UtcNow.AddDays(1)));
        _automatic.Click += (_, _) => Perform(() => _updates.SetAutomaticEnabled(!_updates.Snapshot.AutomaticEnabled));
        _timer.Tick += (_, _) => Refresh();
        Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        var snapshot = _updates.Snapshot;
        _status.Text = Describe(snapshot, DateTime.UtcNow);
        _automatic.Checked = snapshot.AutomaticEnabled;
        _automatic.Enabled = snapshot.BackgroundChecksEnabled && snapshot.CanInstall;
        var busy = snapshot.Status is ApplicationUpdateStatus.Checking or ApplicationUpdateStatus.Downloading or ApplicationUpdateStatus.Restarting;
        _check.Enabled = !busy;
        _apply.Enabled = !busy && snapshot.CanInstall && snapshot.AvailableVersion is not null;
        _defer.Enabled = snapshot.AvailableVersion is not null && snapshot.Status != ApplicationUpdateStatus.Restarting;
        if (snapshot.Status is ApplicationUpdateStatus.Ready or ApplicationUpdateStatus.Available &&
            snapshot.AvailableVersion != _notifiedVersion)
        {
            _notifiedVersion = snapshot.AvailableVersion;
            _icon.ShowBalloonTip(5000, $"{ProductBrand.Name} {snapshot.AvailableVersion}",
                snapshot.CanInstall ? "An update is ready. Use Updates to restart now or postpone it."
                    : "A new release is available. Install the Setup or portable package to enable automatic updates.", ToolTipIcon.Info);
        }
        if (snapshot.Status == ApplicationUpdateStatus.Failed && !_failedNotificationShown)
        {
            _failedNotificationShown = true;
            _icon.ShowBalloonTip(5000, "Update could not be completed", "The app is still running. It will retry in 15 minutes; see the diagnostic log for details.", ToolTipIcon.Warning);
        }
        if (snapshot.Status is ApplicationUpdateStatus.UpToDate or ApplicationUpdateStatus.Ready) _failedNotificationShown = false;
    }

    internal static string Describe(ApplicationUpdateSnapshot snapshot, DateTime nowUtc)
    {
        if (snapshot.Status == ApplicationUpdateStatus.Ready)
        {
            if (snapshot.DeferredUntilUtc > nowUtc) return $"Postponed until {snapshot.DeferredUntilUtc.Value.ToLocalTime():g}";
            if (snapshot.RestartAtUtc is { } restart) return $"Restart in {Math.Max(0, (int)Math.Ceiling((restart - nowUtc).TotalSeconds))} seconds";
            return snapshot.AutomaticEnabled ? "Ready — waiting for the app to be idle" : "Ready — automatic updates are off";
        }
        return snapshot.Status switch
        {
            ApplicationUpdateStatus.Disabled => "Background update checks are disabled",
            ApplicationUpdateStatus.Checking => "Checking for updates…",
            ApplicationUpdateStatus.UpToDate => "You are up to date",
            ApplicationUpdateStatus.Available => $"Version {snapshot.AvailableVersion} available",
            ApplicationUpdateStatus.Downloading => $"Downloading: {snapshot.Progress}%",
            ApplicationUpdateStatus.Restarting => "Applying update and restarting…",
            ApplicationUpdateStatus.Failed => "Update failed — retrying later",
            _ => string.Empty
        };
    }

    private void Perform(Action action)
    {
        try { action(); }
        catch (Exception ex) { _icon.ShowBalloonTip(5000, "Update operation failed", ex.Message, ToolTipIcon.Warning); }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Dispose();
        Menu.Dispose();
    }
}
