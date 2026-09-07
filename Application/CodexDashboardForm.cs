using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexDiscordPresence;

public sealed class CodexDashboardForm : Form
{
    private const int PreviewRegionHeight = 180;

    private readonly PresenceRuntimeState _runtimeState;
    private readonly DashboardOverviewSurface _overviewSurface;
    private readonly DashboardPreviewSurface _previewSurface;
    private readonly System.Windows.Forms.Timer _refreshTimer;

    public CodexDashboardForm(PresenceRuntimeState runtimeState)
    {
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));

        Text = "Codex Discord RPC";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(400, 660);
        Size = MinimumSize;
        BackColor = DashboardPalette.Window;
        ForeColor = DashboardPalette.Text;
        Font = new Font("Segoe UI", 9f);
        AccessibleName = "Codex Discord RPC dashboard";
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Icon = LoadWindowIcon();

        _overviewSurface = new DashboardOverviewSurface { Dock = DockStyle.Fill };
        _previewSurface = new DashboardPreviewSurface
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };

        var overviewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardPalette.SurfaceInset,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        overviewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        overviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, PreviewRegionHeight));
        overviewLayout.Controls.Add(_overviewSurface, 0, 0);
        overviewLayout.Controls.Add(_previewSurface, 0, 1);
        Controls.Add(overviewLayout);

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _refreshTimer.Tick += (_, _) => RefreshSnapshot();
        _refreshTimer.Start();
        FormClosed += (_, _) =>
        {
            _refreshTimer.Dispose();
            Icon?.Dispose();
        };
        Shown += (_, _) => RefreshSnapshot();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        TryUseDarkTitleBar();
    }

    private void RefreshSnapshot()
    {
        var snapshot = _runtimeState.DashboardSnapshot;
        var enabled = _runtimeState.Enabled;
        _overviewSurface.SetSnapshot(snapshot, enabled);
        _previewSurface.SetSnapshot(snapshot, enabled);
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
