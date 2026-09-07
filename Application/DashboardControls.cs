using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal static class DashboardPalette
{
    public static readonly Color Window = Color.FromArgb(18, 19, 23);
    public static readonly Color Surface = Color.FromArgb(28, 30, 36);
    public static readonly Color SurfaceInset = Color.FromArgb(24, 26, 31);
    public static readonly Color SurfaceRaised = Color.FromArgb(36, 38, 46);
    public static readonly Color Border = Color.FromArgb(66, 69, 80);
    public static readonly Color Divider = Color.FromArgb(48, 51, 60);
    public static readonly Color Text = Color.FromArgb(239, 241, 245);
    public static readonly Color MutedText = Color.FromArgb(177, 180, 191);
    public static readonly Color SubtleText = Color.FromArgb(133, 137, 149);
    public static readonly Color Accent = Color.FromArgb(151, 122, 255);
    public static readonly Color AccentSoft = Color.FromArgb(54, 45, 83);
    public static readonly Color Green = Color.FromArgb(92, 196, 135);
    public static readonly Color GreenSoft = Color.FromArgb(35, 68, 51);
    public static readonly Color Disabled = Color.FromArgb(110, 115, 125);
}

internal static class DashboardDrawing
{
    public static void DrawRoundedSurface(
        Graphics graphics,
        Rectangle bounds,
        int radius,
        Color fill,
        Color border)
    {
        using var path = CreateRoundedPath(bounds, radius);
        using var fillBrush = new SolidBrush(fill);
        using var borderPen = new Pen(border);
        graphics.FillPath(fillBrush, path);
        graphics.DrawPath(borderPen, path);
    }

    public static void DrawShadow(Graphics graphics, Rectangle bounds, int radius)
    {
        for (var offset = 8; offset >= 1; offset--)
        {
            var shadowBounds = bounds;
            shadowBounds.Inflate(offset, offset);
            using var path = CreateRoundedPath(shadowBounds, radius + offset);
            using var brush = new SolidBrush(Color.FromArgb(3 + (8 - offset) * 2, 0, 0, 0));
            graphics.FillPath(brush, path);
        }
    }

    public static void DrawText(
        Graphics graphics,
        FontFamily fontFamily,
        string? value,
        Rectangle bounds,
        float size,
        FontStyle style,
        Color color,
        TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis)
    {
        if (string.IsNullOrWhiteSpace(value) || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        using var font = new Font(fontFamily, size, style);
        TextRenderer.DrawText(graphics, value, font, bounds, color, flags);
    }

    public static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var safeRadius = Math.Max(0, Math.Min(radius, Math.Min(bounds.Width, bounds.Height) / 2));
        var diameter = safeRadius * 2;
        var path = new GraphicsPath();

        if (safeRadius == 0)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void DrawValueCard(
        Graphics graphics,
        FontFamily fontFamily,
        Rectangle bounds,
        string value,
        Color accent)
    {
        DrawRoundedSurface(graphics, bounds, 10, DashboardPalette.SurfaceInset, DashboardPalette.Divider);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 10, 3, bounds.Height - 20);
        DrawText(
            graphics,
            fontFamily,
            value,
            new Rectangle(bounds.Left + 18, bounds.Top + 9, bounds.Width - 30, bounds.Height - 18),
            14f,
            FontStyle.Regular,
            DashboardPalette.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    public static void DrawMetricCard(
        Graphics graphics,
        FontFamily fontFamily,
        Rectangle bounds,
        string label,
        string value,
        Color accent)
    {
        DrawRoundedSurface(graphics, bounds, 10, DashboardPalette.SurfaceInset, DashboardPalette.Divider);
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 12, 3, bounds.Height - 24);
        DrawText(
            graphics,
            fontFamily,
            label,
            new Rectangle(bounds.Left + 18, bounds.Top + 9, Math.Min(98, bounds.Width / 3), bounds.Height - 18),
            12f,
            FontStyle.Regular,
            DashboardPalette.SubtleText,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        DrawText(
            graphics,
            fontFamily,
            value,
            new Rectangle(bounds.Left + Math.Min(116, bounds.Width / 3 + 16), bounds.Top + 9, bounds.Width - Math.Min(134, bounds.Width / 3 + 34), bounds.Height - 18),
            14f,
            FontStyle.Regular,
            DashboardPalette.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }
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
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(BackColor);

        var cardWidth = Math.Max(200, ClientSize.Width - 68);
        var availableHeight = Math.Max(220, ClientSize.Height - 64);
        var cardHeight = Math.Min(availableHeight, Math.Max(280, (int)(cardWidth * 0.52f)));
        var cardRect = new Rectangle(
            34,
            Math.Max(24, (ClientSize.Height - cardHeight) / 2),
            cardWidth,
            cardHeight);
        DashboardDrawing.DrawShadow(graphics, cardRect, 18);
        DashboardDrawing.DrawRoundedSurface(graphics, cardRect, 18, DashboardPalette.SurfaceRaised, DashboardPalette.Border);

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "Playing",
            new Rectangle(cardRect.Left + 28, cardRect.Top + 22, cardRect.Width - 88, 24),
            12f,
            FontStyle.Bold,
            DashboardPalette.MutedText);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "...",
            new Rectangle(cardRect.Right - 60, cardRect.Top + 20, 32, 26),
            15f,
            FontStyle.Bold,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);

        var body = new Rectangle(
            cardRect.Left + 42,
            cardRect.Top + 62,
            Math.Max(120, cardRect.Width - 84),
            Math.Max(120, cardRect.Height - 94));
        if (body.Width >= 500)
        {
            DrawWideCard(graphics, body);
        }
        else
        {
            DrawCompactCard(graphics, body);
        }
    }

    private void DrawWideCard(Graphics graphics, Rectangle body)
    {
        var iconSize = Math.Min(190, Math.Max(150, Math.Min(body.Height - 18, body.Width / 2 - 22)));
        var iconRect = new Rectangle(
            body.Left,
            body.Top + 24,
            iconSize,
            iconSize);
        graphics.DrawImage(_codexImage, iconRect);

        var contentLeft = iconRect.Right + 38;
        var contentWidth = Math.Max(120, body.Right - contentLeft);
        var contentTop = body.Top + 28;
        var contentHeight = body.Bottom - contentTop;
        DrawPresenceText(graphics, contentLeft, contentTop, contentWidth, contentHeight);
    }

    private void DrawCompactCard(Graphics graphics, Rectangle body)
    {
        var iconSize = Math.Min(132, Math.Max(96, Math.Min(body.Width - 24, body.Height / 3)));
        var iconRect = new Rectangle(
            body.Left + Math.Max(0, (body.Width - iconSize) / 2),
            body.Top,
            iconSize,
            iconSize);
        graphics.DrawImage(_codexImage, iconRect);

        var contentTop = iconRect.Bottom + 18;
        DrawPresenceText(graphics, body.Left, contentTop, body.Width, body.Bottom - contentTop);
    }

    private void DrawPresenceText(Graphics graphics, int contentLeft, int contentTop, int contentWidth, int contentHeight)
    {
        var presence = _snapshot.Presence;
        var modelProject = DashboardTextFormatter.FormatModelProject(_snapshot);
        var details = string.IsNullOrWhiteSpace(presence?.Details) ? "Waiting for presence update" : presence.Details;
        var state = string.IsNullOrWhiteSpace(presence?.State) ? "Waiting" : presence.State;

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "Codex",
            new Rectangle(contentLeft, contentTop, contentWidth, 34),
            25f,
            FontStyle.Bold,
            DashboardPalette.Text);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            modelProject,
            new Rectangle(contentLeft, contentTop + 44, contentWidth, 42),
            12f,
            FontStyle.Regular,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            details,
            new Rectangle(contentLeft, contentTop + 88, contentWidth, 46),
            13f,
            FontStyle.Regular,
            DashboardPalette.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            state,
            new Rectangle(contentLeft, contentTop + 136, contentWidth, 40),
            14f,
            FontStyle.Regular,
            _enabled ? DashboardPalette.Text : DashboardPalette.Disabled,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        var elapsed = DashboardTextFormatter.FormatElapsed(presence?.StartedAt, DateTime.UtcNow);
        if (elapsed.Length == 0 || contentHeight < 180)
        {
            return;
        }

        var timerTop = contentTop + contentHeight - 36;
        using var timerDot = new SolidBrush(DashboardPalette.Green);
        graphics.FillEllipse(timerDot, contentLeft, timerTop + 8, 9, 9);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            elapsed,
            new Rectangle(contentLeft + 18, timerTop, contentWidth - 18, 30),
            14f,
            FontStyle.Regular,
            DashboardPalette.Green);
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
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
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
        graphics.FillEllipse(dot, 34, 50, 16, 16);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            activity,
            new Rectangle(66, 41, ClientSize.Width - 96, 36),
            16f,
            FontStyle.Bold,
            DashboardPalette.Text);

        var connection = !_enabled
            ? "Presence disabled"
            : _snapshot.IsDiscordConnected
                ? "Connected to Discord"
                : "Connecting to Discord";
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            connection,
            new Rectangle(66, 91, ClientSize.Width - 96, 50),
            13f,
            FontStyle.Regular,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        using var sectionDivider = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(sectionDivider, 34, 184, ClientSize.Width - 34, 184);

        var usage = _snapshot.TokenUsage;
        var valueWidth = Math.Max(150, ClientSize.Width - 68);
        DashboardDrawing.DrawValueCard(
            graphics,
            Font.FontFamily,
            new Rectangle(34, 218, valueWidth, 46),
            DashboardTextFormatter.FormatBillingType(usage?.BillingType),
            DashboardPalette.Accent);
        DashboardDrawing.DrawValueCard(
            graphics,
            Font.FontFamily,
            new Rectangle(34, 278, valueWidth, 46),
            DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit),
            DashboardPalette.Green);
        DashboardDrawing.DrawValueCard(
            graphics,
            Font.FontFamily,
            new Rectangle(34, 338, valueWidth, 46),
            DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow),
            DashboardPalette.Accent);
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
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(BackColor);

        var hero = new Rectangle(40, 32, Math.Max(220, ClientSize.Width - 80), 184);
        DashboardDrawing.DrawShadow(graphics, hero, 16);
        DashboardDrawing.DrawRoundedSurface(graphics, hero, 16, DashboardPalette.SurfaceRaised, DashboardPalette.Border);

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "Current activity",
            new Rectangle(hero.Left + 26, hero.Top + 22, hero.Width - 52, 24),
            12f,
            FontStyle.Bold,
            DashboardPalette.MutedText);

        var activityColor = !_enabled
            ? DashboardPalette.Disabled
            : _snapshot.IsDiscordConnected
                ? DashboardPalette.Green
                : DashboardPalette.Accent;
        using var dot = new SolidBrush(activityColor);
        graphics.FillEllipse(dot, hero.Left + 28, hero.Top + 70, 12, 12);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            DashboardTextFormatter.FormatActivity(_snapshot, _enabled),
            new Rectangle(hero.Left + 52, hero.Top + 58, hero.Width - 78, 36),
            24f,
            FontStyle.Bold,
            DashboardPalette.Text);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            DashboardTextFormatter.FormatModelProject(_snapshot),
            new Rectangle(hero.Left + 28, hero.Top + 112, hero.Width - 56, 42),
            13f,
            FontStyle.Regular,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        var usage = _snapshot.TokenUsage;
        var metrics = new (string Label, string Value, Color Accent)[]
        {
            ("Discord", _snapshot.IsDiscordConnected ? "Connected" : "Disconnected", _snapshot.IsDiscordConnected ? DashboardPalette.Green : DashboardPalette.Accent),
            ("Billing", DashboardTextFormatter.FormatBillingType(usage?.BillingType), DashboardPalette.Accent),
            ("Usage", DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit), DashboardPalette.Green),
            ("Reset", DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow).Replace("reset ", "", StringComparison.Ordinal), DashboardPalette.Accent)
        };

        var left = 40;
        var top = 244;
        var gap = 14;
        var width = Math.Max(220, ClientSize.Width - 80);
        if (width >= 620)
        {
            var columnWidth = (width - gap) / 2;
            for (var index = 0; index < metrics.Length; index++)
            {
                var column = index % 2;
                var row = index / 2;
                var bounds = new Rectangle(left + column * (columnWidth + gap), top + row * 68, columnWidth, 54);
                DashboardDrawing.DrawMetricCard(graphics, Font.FontFamily, bounds, metrics[index].Label, metrics[index].Value, metrics[index].Accent);
            }
        }
        else
        {
            for (var index = 0; index < metrics.Length; index++)
            {
                var bounds = new Rectangle(left, top + index * 62, width, 50);
                DashboardDrawing.DrawMetricCard(graphics, Font.FontFamily, bounds, metrics[index].Label, metrics[index].Value, metrics[index].Accent);
            }
        }
    }
}
