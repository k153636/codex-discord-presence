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
    private bool _exitRequested;

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

        var editMenuItem = new ToolStripMenuItem("Edit Discord RPC");
        editMenuItem.Click += (_, _) => OpenSettingsJson();

        var quitMenuItem = new ToolStripMenuItem("Quit");
        quitMenuItem.Click += (_, _) => RequestExit();

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enableMenuItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(editMenuItem);
        menu.Items.Add(quitMenuItem);

        _notifyIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Codex Discord RPC",
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
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _quitCallback();
        ExitThread();
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
