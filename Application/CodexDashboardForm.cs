using System.Drawing;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexDiscordPresence;

public sealed class CodexDashboardForm : Form
{
    private readonly PresenceRuntimeState _runtimeState;
    private readonly DashboardOverviewSurface _overviewSurface;
    private readonly DashboardStatusRail _statusRail;
    private readonly DashboardPreviewSurface _previewSurface;
    private readonly DashboardTabButton _overviewTab;
    private readonly DashboardTabButton _previewTab;
    private readonly Panel _overviewPage;
    private readonly Panel _previewPage;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private int _selectedTabIndex = 1;

    public CodexDashboardForm(PresenceRuntimeState runtimeState)
    {
        _runtimeState = runtimeState ?? throw new ArgumentNullException(nameof(runtimeState));

        Text = "Codex Discord RPC";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(960, 660);
        MinimumSize = new Size(760, 520);
        BackColor = DashboardPalette.Window;
        ForeColor = DashboardPalette.Text;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;
        Icon = LoadWindowIcon();

        _overviewSurface = new DashboardOverviewSurface { Dock = DockStyle.Fill };
        _statusRail = new DashboardStatusRail { Dock = DockStyle.Fill };
        _previewSurface = new DashboardPreviewSurface { Dock = DockStyle.Fill };

        _overviewPage = CreatePage(_overviewSurface);
        var previewLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardPalette.Window,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 260f));
        previewLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        previewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        previewLayout.Controls.Add(_statusRail, 0, 0);
        previewLayout.Controls.Add(_previewSurface, 1, 0);
        _previewPage = CreatePage(previewLayout);

        _overviewTab = new DashboardTabButton("Overview");
        _previewTab = new DashboardTabButton("Preview");
        _overviewTab.Click += (_, _) => SelectTab(0);
        _previewTab.Click += (_, _) => SelectTab(1);

        var tabStrip = new Panel
        {
            Dock = DockStyle.Top,
            Height = 56,
            BackColor = DashboardPalette.Window,
            Padding = new Padding(0)
        };
        tabStrip.Resize += (_, _) => tabStrip.Invalidate();
        tabStrip.Paint += (_, e) =>
        {
            using var divider = new Pen(DashboardPalette.Divider);
            e.Graphics.DrawLine(divider, 0, tabStrip.ClientSize.Height - 1, tabStrip.ClientSize.Width, tabStrip.ClientSize.Height - 1);
        };

        _overviewTab.Location = new Point(22, 0);
        _previewTab.Location = new Point(182, 0);
        tabStrip.Controls.Add(_overviewTab);
        tabStrip.Controls.Add(_previewTab);

        var pageHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardPalette.Window,
            Padding = new Padding(0)
        };
        pageHost.Controls.Add(_overviewPage);
        pageHost.Controls.Add(_previewPage);
        Controls.Add(pageHost);
        Controls.Add(tabStrip);
        SelectTab(1);

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

    internal IReadOnlyList<string> TabNames => ["Overview", "Preview"];

    internal int SelectedTabIndex => _selectedTabIndex;

    private static Panel CreatePage(Control content)
    {
        var page = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardPalette.Window,
            Padding = new Padding(0)
        };
        page.Controls.Add(content);
        return page;
    }

    private void RefreshSnapshot()
    {
        var snapshot = _runtimeState.DashboardSnapshot;
        var enabled = _runtimeState.Enabled;
        _overviewSurface.SetSnapshot(snapshot, enabled);
        _statusRail.SetSnapshot(snapshot, enabled);
        _previewSurface.SetSnapshot(snapshot, enabled);
    }

    private void SelectTab(int index)
    {
        _selectedTabIndex = index;
        _overviewPage.Visible = index == 0;
        _previewPage.Visible = index == 1;
        _overviewTab.Selected = index == 0;
        _previewTab.Selected = index == 1;
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

internal sealed class DashboardTabButton : Button
{
    private bool _selected;

    public DashboardTabButton(string text)
    {
        Text = text;
        AccessibleName = text;
        AccessibleRole = AccessibleRole.PageTab;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = DashboardPalette.Window;
        ForeColor = DashboardPalette.MutedText;
        Size = new Size(140, 56);
        TabStop = true;
        Cursor = Cursors.Hand;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set
        {
            if (_selected == value)
            {
                return;
            }

            _selected = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Selected ? DashboardPalette.Surface : BackColor);
        using var font = new Font(Font.FontFamily, 13f, Selected ? FontStyle.Bold : FontStyle.Regular);
        var textColor = Selected ? DashboardPalette.Text : DashboardPalette.MutedText;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            font,
            new Rectangle(8, 17, ClientSize.Width - 16, 26),
            textColor,
            TextFormatFlags.NoPadding);

        if (Selected)
        {
            using var accent = new SolidBrush(DashboardPalette.Accent);
            e.Graphics.FillRectangle(accent, 4, ClientSize.Height - 3, ClientSize.Width - 8, 3);
        }
        else if (ClientRectangle.Contains(PointToClient(Cursor.Position)))
        {
            using var hover = new SolidBrush(DashboardPalette.Surface);
            e.Graphics.FillRectangle(hover, 4, ClientSize.Height - 3, ClientSize.Width - 8, 3);
        }
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        base.OnMouseEnter(e);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        Invalidate();
    }
}
