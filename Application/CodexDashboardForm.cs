using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexDiscordPresence;

public sealed class CodexDashboardForm : Form
{
    private const int PreviewRegionHeight = 178;

    private readonly PresenceRuntimeState _runtimeState;
    private readonly DashboardOverviewSurface _overviewSurface;
    private readonly DashboardPreviewSurface _previewSurface;
    private readonly ProviderIntegrationPanel _providerPanel;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly PresenceStateStore _stateStore;
    private readonly string _statePath;
    private bool _syncingProviderControls;
    private bool _resourcesDisposed;

    public CodexDashboardForm(PresenceRuntimeState runtimeState)
        : this(runtimeState, new PresenceStateStore(), PresenceStateStore.GetDefaultPath())
    {
    }

    public CodexDashboardForm(
        PresenceRuntimeState runtimeState,
        PresenceStateStore stateStore,
        string statePath)
    {
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _statePath = string.IsNullOrWhiteSpace(statePath)
            ? throw new ArgumentException("A state path is required.", nameof(statePath))
            : statePath;

        Text = ProductBrand.Name;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(384, 344);
        MinimumSize = SizeFromClientSize(ClientSize);
        BackColor = DashboardPalette.Window;
        ForeColor = DashboardPalette.Text;
        Font = new Font("Segoe UI", 9f);
        AccessibleName = $"{ProductBrand.Name} dashboard";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Icon = LoadWindowIcon();

        _overviewSurface = new DashboardOverviewSurface { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _previewSurface = new DashboardPreviewSurface
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        _providerPanel = new ProviderIntegrationPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty
        };
        _providerPanel.ProviderEnabledChanged += OnProviderEnabledChanged;
        SyncProviderControls();

        var overviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardPalette.Window,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0),
            Padding = new Padding(12)
        };
        overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, ProviderIntegrationPanel.PreferredHeight));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewRegionHeight));
        overviewLayout.Controls.Add(_providerPanel, 0, 0);
        overviewLayout.Controls.Add(_overviewSurface, 0, 1);
        overviewLayout.Controls.Add(_previewSurface, 0, 2);
        Controls.Add(overviewLayout);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        _refreshTimer.Start();
        Shown += (_, _) => RefreshSnapshot();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_resourcesDisposed)
        {
            _resourcesDisposed = true;
            _refreshTimer?.Stop();
            _refreshTimer?.Dispose();
            if (_providerPanel is not null) _providerPanel.ProviderEnabledChanged -= OnProviderEnabledChanged;
            Icon?.Dispose();
            Icon = null;
        }
        base.Dispose(disposing);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryUseDarkTitleBar();
    }

    private void RefreshSnapshot()
    {
        if (IsDisposed || Disposing) return;
        var snapshot = _runtimeState.DashboardSnapshot;
        var enabled = _runtimeState.Enabled;
        SyncProviderControls();
        _providerPanel.SetOwner(!enabled || snapshot.HasNoActiveProvider || snapshot.Presence is null ? null : snapshot.ProviderId);
        var layout = (TableLayoutPanel)Controls[0];
        var previewHeight = DashboardPreviewSurface.GetPreferredHeight(snapshot);
        var previousMinimum = MinimumSize;
        var scale = DeviceDpi / 96f;
        MinimumSize = SizeFromClientSize(new Size((int)Math.Round(384 * scale), (int)Math.Round((166 + previewHeight) * scale)));
        layout.RowStyles[2].Height = previewHeight * DeviceDpi / 96f;
        if (Size == previousMinimum) Size = MinimumSize;

        _overviewSurface.SetSnapshot(snapshot, enabled);
        _previewSurface.SetSnapshot(snapshot, enabled);
    }

    private void OnProviderEnabledChanged(object? sender, ProviderEnabledChangedEventArgs e)
    {
        if (_syncingProviderControls)
        {
            return;
        }

        _runtimeState.SetProviderEnabled(e.ProviderId, e.Enabled);
        _stateStore.Save(_statePath, _runtimeState);
    }

    private void SyncProviderControls()
    {
        _syncingProviderControls = true;
        try
        {
            _providerPanel.ApplyProviderState(
                _runtimeState.IsProviderEnabled(ProviderIds.Codex, defaultValue: true),
                _runtimeState.IsProviderEnabled(ProviderIds.Antigravity, defaultValue: false),
                _runtimeState.IsProviderEnabled(ProviderIds.ClaudeCode, defaultValue: false));
        }
        finally
        {
            _syncingProviderControls = false;
        }
    }

    private void TryUseDarkTitleBar()
    {
        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
            _ = DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));

            var captionColor = ToColorRef(DashboardPalette.Window);
            var borderColor = ToColorRef(DashboardPalette.Border);
            var textColor = ToColorRef(DashboardPalette.Text);
            _ = DwmSetWindowAttribute(Handle, 35, ref captionColor, sizeof(int));
            _ = DwmSetWindowAttribute(Handle, 34, ref borderColor, sizeof(int));
            _ = DwmSetWindowAttribute(Handle, 36, ref textColor, sizeof(int));
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static int ToColorRef(Color color)
    {
        return color.R | (color.G << 8) | (color.B << 16);
    }

    private static System.Drawing.Icon? LoadWindowIcon()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpcArt", "rpc_codex.png");
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            using var source = Image.FromFile(path);
            using var bitmap = new Bitmap(source, new Size(32, 32));
            var handle = bitmap.GetHicon();
            try
            {
                using var icon = System.Drawing.Icon.FromHandle(handle);
                return (System.Drawing.Icon)icon.Clone();
            }
            finally
            {
                _ = DestroyIcon(handle);
            }
        }
        catch
        {
            return null;
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
