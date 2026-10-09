using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal static class DashboardPalette
{
    public static readonly Color Window = Color.FromArgb(11, 12, 14);
    public static readonly Color SurfaceInset = Window;
    public static readonly Color Surface = Color.FromArgb(18, 19, 23);
    public static readonly Color Border = Color.FromArgb(44, 46, 52);
    public static readonly Color Text = Color.FromArgb(239, 241, 245);
    public static readonly Color MutedText = Color.FromArgb(177, 180, 191);
    public static readonly Color SubtleText = Color.FromArgb(154, 158, 171);
    public static readonly Color Accent = Color.FromArgb(151, 122, 255);
    public static readonly Color Green = Color.FromArgb(92, 196, 135);
    public static readonly Color Amber = Color.FromArgb(227, 179, 65);
    public static readonly Color Red = Color.FromArgb(242, 135, 122);
    public static readonly Color FocusRing = Color.FromArgb(196, 181, 255);
    public static readonly Color DiscordCardTop = Color.FromArgb(58, 58, 60);
    public static readonly Color DiscordCardBottom = Color.FromArgb(26, 26, 28);
    public static readonly Color DiscordText = Color.FromArgb(219, 221, 223);
    public static readonly Color DiscordGreen = Color.FromArgb(126, 193, 145);
}

internal readonly record struct DashboardMetric(string Label, string Value, Color Accent, double? ProgressPercent = null);

internal static class DashboardLayoutMetrics
{
    public const int PreferredContentInset = 12;
    public const int PreviewCardWidth = 360;
    public static int GetHorizontalInset(int clientWidth) => Math.Min(12, Math.Max(0, (clientWidth - 360) / 2));
    public static int GetContentLeft(int clientWidth) => GetHorizontalInset(clientWidth);
    public static int GetContentWidth(int clientWidth) => Math.Max(1, clientWidth - GetHorizontalInset(clientWidth) * 2);
}

internal static class DashboardProviderPresentation
{
    public static Color Accent(string? providerId) => providerId switch
    {
        ProviderIds.Codex => DashboardPalette.Accent,
        ProviderIds.ClaudeCode => Color.FromArgb(217, 119, 87),
        ProviderIds.Antigravity => Color.FromArgb(79, 141, 247),
        _ => DashboardPalette.SubtleText
    };

    public static string? IconReference(string? providerId) => providerId switch
    {
        ProviderIds.Codex => "rpc_codex",
        ProviderIds.ClaudeCode => "clawd-icon.png",
        ProviderIds.Antigravity => "rpc_antigravity_cli",
        _ => null
    };

    public static Image LoadIcon(string? providerId)
    {
        var reference = IconReference(providerId);
        using var source = reference is null ? null : DashboardPresenceImageSlot.LoadLocalImage(reference);
        return source is null ? SystemIcons.Application.ToBitmap() : new Bitmap(source);
    }
}

internal sealed class ProviderEnabledChangedEventArgs(string providerId, bool enabled) : EventArgs
{
    public string ProviderId { get; } = providerId;
    public bool Enabled { get; } = enabled;
}

internal sealed class ProviderIntegrationPanel : Panel
{
    public const int PreferredHeight = 54;
    private readonly ProviderChip _codex = new(ProviderIds.Codex, "Codex", 0);
    private readonly ProviderChip _claude = new(ProviderIds.ClaudeCode, "Claude Code", 1);
    private readonly ProviderChip _antigravity = new(ProviderIds.Antigravity, "Antigravity CLI", 2);

    public ProviderIntegrationPanel()
    {
        AccessibleName = "Provider integration settings";
        BackColor = DashboardPalette.Window;
        Padding = new Padding(0, 0, 0, 10);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = BackColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27.33f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 39.34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var chip in new[] { _codex, _claude, _antigravity })
        {
            chip.Dock = DockStyle.Fill;
            chip.Margin = chip == _antigravity ? Padding.Empty : new Padding(0, 0, 6, 0);
            chip.CheckedChanged += (_, _) => ProviderEnabledChanged?.Invoke(this, new(chip.ProviderId, chip.Checked));
            layout.Controls.Add(chip);
        }
        Controls.Add(layout);
    }

    public event EventHandler<ProviderEnabledChangedEventArgs>? ProviderEnabledChanged;
    public CheckBox CodexCheckBox => _codex;
    public CheckBox ClaudeCodeCheckBox => _claude;
    public CheckBox AntigravityCheckBox => _antigravity;

    public void ApplyProviderState(bool codexEnabled, bool antigravityEnabled, bool claudeCodeEnabled = false)
    {
        _codex.Checked = codexEnabled;
        _antigravity.Checked = antigravityEnabled;
        _claude.Checked = claudeCodeEnabled;
    }

    public void SetOwner(string? providerId)
    {
        foreach (var chip in new[] { _codex, _claude, _antigravity }) chip.IsOwner = chip.ProviderId == providerId;
    }
}

internal sealed class ProviderChip : CheckBox
{
    private readonly Image _icon;
    private bool _isOwner;
    public string ProviderId { get; }

    public ProviderChip(string providerId, string name, int tabIndex)
    {
        ProviderId = providerId;
        Text = name;
        TabIndex = tabIndex;
        AccessibleName = providerId == ProviderIds.Antigravity ? "Antigravity integration" : name + " integration";
        AccessibleDescription = "Off";
        BackColor = DashboardPalette.Window;
        _icon = DashboardProviderPresentation.LoadIcon(providerId);
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool IsOwner
    {
        get => _isOwner;
        set { if (_isOwner != value) { _isOwner = value; UpdateStatus(); } }
    }

    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); UpdateStatus(); }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override void Dispose(bool disposing) { if (disposing) _icon.Dispose(); base.Dispose(disposing); }

    private void UpdateStatus()
    {
        AccessibleDescription = Checked ? IsOwner ? "Live" : "Standby" : "Off";
        AccessibilityNotifyClients(AccessibleEvents.DescriptionChange, -1);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var width = ClientSize.Width / scale;
        var height = ClientSize.Height / scale;
        var accent = DashboardProviderPresentation.Accent(ProviderId);
        var live = Checked && IsOwner;
        var bounds = Rectangle.Round(new RectangleF(1, 1, width - 2, height - 2));
        DashboardDrawing.DrawRoundedSurface(g, bounds, 8, live ? Color.FromArgb(30, accent) : DashboardPalette.Surface,
            live ? accent : DashboardPalette.Border);
        if (live) DashboardDrawing.DrawRoundedSurface(g, Rectangle.Inflate(bounds, -2, -2), 6, Color.Transparent, Color.FromArgb(75, accent));
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        DashboardDrawing.DrawImage(g, _icon, new Rectangle(8, 8, 16, 16), !Checked);
        DashboardDrawing.DrawText(g, Font.FontFamily, Text, new Rectangle(30, 5, (int)width - 35, 17), 9, FontStyle.Bold,
            Checked ? DashboardPalette.Text : DashboardPalette.SubtleText);
        var check = new Rectangle(9, 27, 10, 10);
        using var pen = new Pen(Checked ? accent : DashboardPalette.SubtleText, 1.3f);
        g.DrawRectangle(pen, check);
        if (Checked) g.DrawLines(pen, [new Point(11, 32), new Point(13, 34), new Point(17, 29)]);
        DashboardDrawing.DrawText(g, Font.FontFamily, AccessibleDescription, new Rectangle(25, 24, (int)width - 30, 16), 8.25f,
            live ? FontStyle.Bold : FontStyle.Regular, live ? DashboardPalette.Text : DashboardPalette.SubtleText);
        if (Focused && ShowFocusCues)
        {
            using var focus = new Pen(DashboardPalette.FocusRing, 2);
            using var path = DashboardDrawing.CreateRoundedPath(Rectangle.Inflate(bounds, -3, -3), 6);
            g.DrawPath(focus, path);
        }
    }
}

internal abstract class DashboardSurface : Control
{
    protected DashboardSurface()
    {
        BackColor = DashboardPalette.Window;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.Clear(BackColor);
        var scale = DeviceDpi / 96f;
        g.ScaleTransform(scale, scale);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        DrawSurface(g, new Size((int)Math.Round(ClientSize.Width / scale), (int)Math.Round(ClientSize.Height / scale)));
    }

    protected abstract void DrawSurface(Graphics graphics, Size size);
}

internal static class DashboardDrawing
{
    public static void DrawRoundedSurface(Graphics graphics, Rectangle bounds, int radius, Color fill, Color border)
    {
        using var path = CreateRoundedPath(bounds, radius);
        using var brush = new SolidBrush(fill);
        using var pen = new Pen(border);
        graphics.FillPath(brush, path);
        graphics.DrawPath(pen, path);
    }

    public static void DrawText(Graphics graphics, FontFamily family, string? text, Rectangle bounds,
        float size, FontStyle style, Color color)
    {
        if (string.IsNullOrWhiteSpace(text) || bounds.Width <= 0 || bounds.Height <= 0) return;
        using var font = new Font(family, size * 4 / 3, style, GraphicsUnit.Pixel);
        using var brush = new SolidBrush(color);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap,
            Trimming = StringTrimming.EllipsisCharacter,
            LineAlignment = StringAlignment.Center
        };
        graphics.DrawString(text, font, brush, bounds, format);
    }

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = Math.Max(1, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawImage(Graphics graphics, Image image, Rectangle bounds, bool dim = false)
    {
        using var attributes = new System.Drawing.Imaging.ImageAttributes();
        if (dim) attributes.SetColorMatrix(new System.Drawing.Imaging.ColorMatrix { Matrix33 = 0.5f });
        graphics.DrawImage(image, bounds, 0, 0, image.Width, image.Height, GraphicsUnit.Pixel, attributes);
    }
}
