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
        ClientSize = new Size(900, 640);
        MinimumSize = new Size(720, 480);
        BackColor = DashboardPalette.Window;
        ForeColor = DashboardPalette.Text;
        Font = new Font("Segoe UI", 9f);
        AutoScaleMode = AutoScaleMode.Dpi;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = true;
        MinimizeBox = true;

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
        FormClosed += (_, _) => _refreshTimer.Dispose();
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
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
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
        e.Graphics.Clear(BackColor);
        using var font = new Font(Font.FontFamily, 14f, Selected ? FontStyle.Bold : FontStyle.Regular);
        var textColor = Selected ? DashboardPalette.Text : DashboardPalette.MutedText;
        TextRenderer.DrawText(
            e.Graphics,
            Text,
            font,
            new Rectangle(8, 16, ClientSize.Width - 16, 28),
            textColor,
            TextFormatFlags.NoPadding);

        if (Selected)
        {
            using var accent = new SolidBrush(DashboardPalette.Accent);
            e.Graphics.FillRectangle(accent, 4, ClientSize.Height - 4, ClientSize.Width - 8, 4);
        }
    }
}
