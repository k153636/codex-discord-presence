using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace CodexDiscordPresence;

public sealed class TrayIconHost : ApplicationContext
{
    private readonly PresenceRuntimeState _state;
    private readonly PresenceStateStore _stateStore;
    private readonly string _statePath;
    private readonly string _settingsPath;
    private readonly Action _quitCallback;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enableMenuItem;
    private CodexDashboardForm? _dashboardForm;
    private bool _exitRequested;
    private int _dashboardOpen;
    private ApplicationUpdateMenu? _updateMenu;

    internal bool IsDashboardOpen => Volatile.Read(ref _dashboardOpen) != 0;

    internal void AttachUpdates(ApplicationUpdateCoordinator updates)
    {
        _updateMenu = new ApplicationUpdateMenu(updates, _notifyIcon);
        _notifyIcon.ContextMenuStrip!.Items.Insert(3, _updateMenu.Menu);
        if (Program.RestartedAfterUpdate)
        {
            _notifyIcon.ShowBalloonTip(5000, ProductBrand.Name, $"Updated to {AppVersion.Current}. Your settings have been retained.", ToolTipIcon.Info);
        }
    }

    public TrayIconHost(
        PresenceRuntimeState state,
        PresenceStateStore stateStore,
        string statePath,
        string settingsPath,
        Action quitCallback)
    {
        _state = state;
        _stateStore = stateStore;
        _statePath = statePath;
        _settingsPath = settingsPath;
        _quitCallback = quitCallback;

        _enableMenuItem = new ToolStripMenuItem();
        _enableMenuItem.Click += (_, _) => ToggleEnabled();

        var openDashboardMenuItem = new ToolStripMenuItem("Open Dashboard");
        openDashboardMenuItem.Click += (_, _) => OpenDashboard();

        var editMenuItem = new ToolStripMenuItem($"Edit {ProductBrand.Name} settings");
        editMenuItem.Click += (_, _) => OpenSettingsJson();

        var quitMenuItem = new ToolStripMenuItem("Quit");
        quitMenuItem.Click += (_, _) => RequestExit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enableMenuItem);
        menu.Items.Add(openDashboardMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(editMenuItem);
        menu.Items.Add(quitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = ProductBrand.Name,
            Visible = true,
            ContextMenuStrip = menu
        };

        _notifyIcon.DoubleClick += (_, _) => ToggleEnabled();
        UpdateMenu();
    }

    public void RequestExit()
    {
        if (_exitRequested)
        {
            return;
        }

        _exitRequested = true;
        _stateStore.Save(_statePath, _state);
        _updateMenu?.Dispose();
        _updateMenu = null;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _dashboardForm?.Close();
        _dashboardForm = null;
        Interlocked.Exchange(ref _dashboardOpen, 0);
        _quitCallback();
        ExitThread();
    }

    public void OpenDashboard()
    {
        if (_dashboardForm is null || _dashboardForm.IsDisposed)
        {
            var dashboard = new CodexDashboardForm(_state, _stateStore, _statePath);
            dashboard.FormClosed += (_, _) =>
            {
                if (ReferenceEquals(_dashboardForm, dashboard))
                {
                    _dashboardForm = null;
                    Interlocked.Exchange(ref _dashboardOpen, 0);
                }
            };
            _dashboardForm = dashboard;
            Interlocked.Exchange(ref _dashboardOpen, 1);
        }

        if (_dashboardForm.WindowState == FormWindowState.Minimized)
        {
            _dashboardForm.WindowState = FormWindowState.Normal;
        }

        _dashboardForm.Show();
        _dashboardForm.BringToFront();
        _dashboardForm.Activate();
    }

    private void ToggleEnabled()
    {
        _state.Enabled = !_state.Enabled;
        _stateStore.Save(_statePath, _state);
        UpdateMenu();
    }

    private void UpdateMenu()
    {
        _enableMenuItem.Text = "Enable";
        _enableMenuItem.Checked = _state.Enabled;
    }

    private void OpenSettingsJson()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                Console.Error.WriteLine($"Settings JSON not found: {_settingsPath}");
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = _settingsPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open settings JSON: {ex.Message}");
        }
    }
}
