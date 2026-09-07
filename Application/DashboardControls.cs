using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal static class DashboardPalette
{
    public static readonly Color Window = Color.FromArgb(20, 22, 25);
    public static readonly Color Surface = Color.FromArgb(29, 32, 37);
    public static readonly Color SurfaceRaised = Color.FromArgb(35, 38, 44);
    public static readonly Color Border = Color.FromArgb(57, 61, 69);
    public static readonly Color Divider = Color.FromArgb(48, 52, 59);
    public static readonly Color Text = Color.FromArgb(239, 241, 245);
    public static readonly Color MutedText = Color.FromArgb(165, 169, 179);
    public static readonly Color Accent = Color.FromArgb(139, 113, 255);
    public static readonly Color Green = Color.FromArgb(92, 196, 135);
    public static readonly Color Disabled = Color.FromArgb(110, 115, 125);
}

internal sealed class DashboardPreviewSurface : Control
{
    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;
    private readonly Image _codexImage;

    public DashboardPreviewSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = DashboardPalette.Window;
        _codexImage = LoadCodexImage();
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _codexImage.Dispose();
        }

        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.Clear(BackColor);

        var cardRect = new Rectangle(
            42,
            54,
            Math.Max(120, ClientSize.Width - 84),
            Math.Max(180, ClientSize.Height - 108));
        DrawRoundedSurface(graphics, cardRect, 18);

        DrawText(graphics, "Playing", new Rectangle(cardRect.Left + 28, cardRect.Top + 24, cardRect.Width - 80, 26), 15f, FontStyle.Bold, DashboardPalette.MutedText);
        DrawText(graphics, "...", new Rectangle(cardRect.Right - 58, cardRect.Top + 22, 30, 28), 15f, FontStyle.Bold, DashboardPalette.MutedText, TextFormatFlags.HorizontalCenter);

        var iconSize = Math.Min(148, Math.Max(92, cardRect.Height - 184));
        var iconRect = new Rectangle(
            cardRect.Left + 44,
            cardRect.Top + Math.Max(78, (cardRect.Height - iconSize) / 2),
            iconSize,
            iconSize);
        graphics.DrawImage(_codexImage, iconRect);

        var contentLeft = iconRect.Right + 38;
        var contentWidth = Math.Max(100, cardRect.Right - contentLeft - 34);
        var contentTop = cardRect.Top + 98;
        DrawText(graphics, "Codex", new Rectangle(contentLeft, contentTop, contentWidth, 38), 25f, FontStyle.Bold, DashboardPalette.Text);

        var modelProject = DashboardTextFormatter.FormatModelProject(_snapshot);
        var presence = _snapshot.Presence;
        var details = string.IsNullOrWhiteSpace(presence?.Details) ? "Waiting for presence update" : presence.Details;
        var state = string.IsNullOrWhiteSpace(presence?.State) ? "Waiting" : presence.State;

        DrawText(graphics, modelProject, new Rectangle(contentLeft, contentTop + 48, contentWidth, 42), 12f, FontStyle.Regular, DashboardPalette.MutedText, TextFormatFlags.WordBreak);
        DrawText(graphics, details, new Rectangle(contentLeft, contentTop + 91, contentWidth, 46), 13f, FontStyle.Regular, DashboardPalette.Text, TextFormatFlags.WordBreak);
        DrawText(graphics, state, new Rectangle(contentLeft, contentTop + 143, contentWidth, 44), 14f, FontStyle.Regular, DashboardPalette.Text, TextFormatFlags.WordBreak);

        var elapsed = DashboardTextFormatter.FormatElapsed(presence?.StartedAt, DateTime.UtcNow);
        if (elapsed.Length > 0)
        {
            DrawText(graphics, elapsed, new Rectangle(contentLeft, cardRect.Bottom - 78, contentWidth, 30), 14f, FontStyle.Regular, DashboardPalette.Green);
        }
    }

    private void DrawRoundedSurface(Graphics graphics, Rectangle bounds, int radius)
    {
        using var path = CreateRoundedPath(bounds, radius);
        using var fill = new SolidBrush(DashboardPalette.SurfaceRaised);
        using var border = new Pen(DashboardPalette.Border);
        graphics.FillPath(fill, path);
        graphics.DrawPath(border, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void DrawText(
        Graphics graphics,
        string? value,
        Rectangle bounds,
        float size,
        FontStyle style,
        Color color,
        TextFormatFlags additionalFlags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis)
    {
        if (string.IsNullOrWhiteSpace(value) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var font = new Font(Font.FontFamily, size, style);
        TextRenderer.DrawText(graphics, value, font, bounds, color, additionalFlags);
    }

    private static Image LoadCodexImage()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpcArt", "rpc_codex.png");
        try
        {
            if (File.Exists(path))
            {
                using var source = Image.FromFile(path);
                return new Bitmap(source);
            }
        }
        catch
        {
        }

        return SystemIcons.Application.ToBitmap();
    }
}

internal sealed class DashboardStatusRail : Control
{
    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;

    public DashboardStatusRail()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = DashboardPalette.Window;
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.Clear(BackColor);
        using var divider = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(divider, ClientSize.Width - 1, 0, ClientSize.Width - 1, ClientSize.Height);

        var activity = DashboardTextFormatter.FormatActivity(_snapshot, _enabled);
        var statusColor = !_enabled
            ? DashboardPalette.Disabled
            : _snapshot.IsDiscordConnected
                ? DashboardPalette.Green
                : DashboardPalette.Accent;

        using var dot = new SolidBrush(statusColor);
        graphics.FillEllipse(dot, 40, 56, 18, 18);
        DrawText(graphics, activity, new Rectangle(76, 48, ClientSize.Width - 100, 38), 16f, FontStyle.Regular, DashboardPalette.Text);

        var connection = !_enabled
            ? "Presence disabled"
            : _snapshot.IsDiscordConnected
                ? "Connected to Discord"
                : "Connecting to Discord";
        DrawText(graphics, connection, new Rectangle(76, 104, ClientSize.Width - 100, 56), 15f, FontStyle.Regular, DashboardPalette.Text, TextFormatFlags.WordBreak);

        using var dividerPen = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(dividerPen, 40, 212, ClientSize.Width - 40, 212);

        var usage = _snapshot.TokenUsage;
        DrawText(graphics, DashboardTextFormatter.FormatBillingType(usage?.BillingType), new Rectangle(40, 260, ClientSize.Width - 80, 32), 15f, FontStyle.Regular, DashboardPalette.Text);
        DrawText(graphics, DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit), new Rectangle(40, 314, ClientSize.Width - 80, 32), 15f, FontStyle.Regular, DashboardPalette.Text);
        DrawText(graphics, DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow), new Rectangle(40, 368, ClientSize.Width - 80, 32), 15f, FontStyle.Regular, DashboardPalette.Text);
    }

    private void DrawText(
        Graphics graphics,
        string? value,
        Rectangle bounds,
        float size,
        FontStyle style,
        Color color,
        TextFormatFlags additionalFlags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis)
    {
        if (string.IsNullOrWhiteSpace(value) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var font = new Font(Font.FontFamily, size, style);
        TextRenderer.DrawText(graphics, value, font, bounds, color, additionalFlags);
    }
}

internal sealed class DashboardOverviewSurface : Control
{
    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;

    public DashboardOverviewSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = DashboardPalette.Window;
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var graphics = e.Graphics;
        graphics.Clear(BackColor);

        DrawText(graphics, "Current activity", new Rectangle(48, 48, ClientSize.Width - 96, 36), 18f, FontStyle.Bold, DashboardPalette.Text);
        DrawText(graphics, DashboardTextFormatter.FormatActivity(_snapshot, _enabled), new Rectangle(48, 98, ClientSize.Width - 96, 48), 25f, FontStyle.Bold, DashboardPalette.Text);
        DrawText(graphics, DashboardTextFormatter.FormatModelProject(_snapshot), new Rectangle(48, 154, ClientSize.Width - 96, 42), 14f, FontStyle.Regular, DashboardPalette.MutedText, TextFormatFlags.WordBreak);

        using var divider = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(divider, 48, 232, ClientSize.Width - 48, 232);

        var usage = _snapshot.TokenUsage;
        DrawRow(graphics, "Discord", _snapshot.IsDiscordConnected ? "Connected" : "Disconnected", 278);
        DrawRow(graphics, "Billing", DashboardTextFormatter.FormatBillingType(usage?.BillingType), 330);
        DrawRow(graphics, "Usage", DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit), 382);
        DrawRow(graphics, "Reset", DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow).Replace("reset ", "", StringComparison.Ordinal), 434);
    }

    private void DrawRow(Graphics graphics, string label, string value, int y)
    {
        DrawText(graphics, label, new Rectangle(48, y, 170, 30), 14f, FontStyle.Regular, DashboardPalette.MutedText);
        DrawText(graphics, value, new Rectangle(220, y, ClientSize.Width - 268, 30), 14f, FontStyle.Regular, DashboardPalette.Text);
    }

    private void DrawText(
        Graphics graphics,
        string? value,
        Rectangle bounds,
        float size,
        FontStyle style,
        Color color,
        TextFormatFlags additionalFlags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis)
    {
        if (string.IsNullOrWhiteSpace(value) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var font = new Font(Font.FontFamily, size, style);
        TextRenderer.DrawText(graphics, value, font, bounds, color, additionalFlags);
    }
}
