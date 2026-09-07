using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal static class DashboardPalette
{
    public static readonly Color Window = Color.FromArgb(18, 19, 23);
    public static readonly Color Surface = Color.FromArgb(28, 30, 36);
    public static readonly Color SurfaceInset = Color.FromArgb(24, 26, 31);
    public static readonly Color SurfaceRaised = Color.FromArgb(32, 33, 38);
    public static readonly Color Border = Color.FromArgb(44, 46, 52);
    public static readonly Color Divider = Color.FromArgb(48, 51, 60);
    public static readonly Color Text = Color.FromArgb(239, 241, 245);
    public static readonly Color MutedText = Color.FromArgb(177, 180, 191);
    public static readonly Color SubtleText = Color.FromArgb(133, 137, 149);
    public static readonly Color Accent = Color.FromArgb(151, 122, 255);
    public static readonly Color AccentSoft = Color.FromArgb(54, 45, 83);
    public static readonly Color Green = Color.FromArgb(92, 196, 135);
    public static readonly Color GreenSoft = Color.FromArgb(35, 68, 51);
    public static readonly Color Disabled = Color.FromArgb(110, 115, 125);
    public static readonly Color DiscordCardTop = Color.FromArgb(58, 58, 60);
    public static readonly Color DiscordCardBottom = Color.FromArgb(26, 26, 28);
    public static readonly Color DiscordText = Color.FromArgb(219, 221, 223);
    public static readonly Color DiscordGreen = Color.FromArgb(126, 193, 145);
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

    public static void DrawValueRow(
        Graphics graphics,
        FontFamily fontFamily,
        Rectangle bounds,
        string value)
    {
        DrawText(
            graphics,
            fontFamily,
            value,
            new Rectangle(bounds.Left, bounds.Top + 9, bounds.Width, bounds.Height - 18),
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
    private readonly Image _fallbackImage;
    private readonly Image _gameIcon;
    private readonly DashboardPresenceImageSlot _largeImage;
    private readonly DashboardPresenceImageSlot _smallImage;

    public DashboardPreviewSurface()
    {
        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint,
            true);
        BackColor = DashboardPalette.Window;
        _fallbackImage = LoadCodexImage();
        _gameIcon = DashboardDiscordActivityIcon.Load();
        _largeImage = new DashboardPresenceImageSlot(_fallbackImage, InvalidateIfAlive);
        _smallImage = new DashboardPresenceImageSlot(_fallbackImage, InvalidateIfAlive);
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        _largeImage.SetReference(snapshot.PublishedPresence?.LargeImageKey);
        _smallImage.SetReference(snapshot.PublishedPresence?.SmallImageKey);

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _largeImage.Dispose();
            _smallImage.Dispose();
            _gameIcon.Dispose();
            _fallbackImage.Dispose();
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

        var renderedWidth = 360;
        var renderedHeight = 172;
        var origin = new Point(
            Math.Max(24, (ClientSize.Width - renderedWidth) / 2),
            Math.Max(24, (ClientSize.Height - renderedHeight) / 2));

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "現在のアクティビティ",
            new Rectangle(origin.X, origin.Y, 360, 20),
            10f,
            FontStyle.Regular,
            DashboardPalette.DiscordText);
        DrawDiscordActivityCard(graphics, new Rectangle(origin.X, origin.Y + 24, 360, 148));
    }

    private void DrawDiscordActivityCard(Graphics graphics, Rectangle cardRect)
    {
        var publishedPresence = _snapshot.PublishedPresence;
        var presence = _snapshot.Presence;
        var details = publishedPresence is not null
            ? publishedPresence.Details
            : string.IsNullOrWhiteSpace(presence?.Details) ? "Waiting for presence update" : presence.Details;
        var state = publishedPresence is not null
            ? publishedPresence.State
            : string.IsNullOrWhiteSpace(presence?.State) ? "Waiting" : presence.State;
        var startedAt = publishedPresence is not null
            ? publishedPresence.StartedAtUtc
            : presence?.StartedAt;

        using (var cardPath = DashboardDrawing.CreateRoundedPath(cardRect, 14))
        using (var cardBrush = new LinearGradientBrush(
                   cardRect,
                   DashboardPalette.DiscordCardTop,
                   DashboardPalette.DiscordCardBottom,
                   LinearGradientMode.Vertical))
        {
            graphics.FillPath(cardBrush, cardPath);
        }

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "プレイ中：",
            new Rectangle(cardRect.Left + 12, cardRect.Top + 11, 160, 18),
            10f,
            FontStyle.Bold,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "...",
            new Rectangle(cardRect.Right - 38, cardRect.Top + 6, 30, 20),
            10f,
            FontStyle.Bold,
            DashboardPalette.DiscordText,
            TextFormatFlags.NoPadding | TextFormatFlags.HorizontalCenter);

        var largeImageRect = new Rectangle(cardRect.Left + 12, cardRect.Top + 36, 100, 100);
        DrawRoundedImage(graphics, _largeImage.CurrentImage, largeImageRect, 8);
        var smallImageRect = new Rectangle(cardRect.Left + 84, cardRect.Top + 104, 32, 32);
        using (var smallImageBorder = new SolidBrush(DashboardPalette.DiscordCardBottom))
        {
            graphics.FillEllipse(
                smallImageBorder,
                smallImageRect.Left - 2,
                smallImageRect.Top - 2,
                smallImageRect.Width + 4,
                smallImageRect.Height + 4);
        }
        DrawCircularImage(graphics, _smallImage.CurrentImage, smallImageRect);

        var contentLeft = cardRect.Left + 123;
        var contentWidth = cardRect.Width - 135;
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "Codex",
            new Rectangle(contentLeft, cardRect.Top + 51, contentWidth, 18),
            11f,
            FontStyle.Bold,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            details,
            new Rectangle(contentLeft, cardRect.Top + 69, contentWidth, 17),
            9f,
            FontStyle.Regular,
            DashboardPalette.DiscordText,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            state,
            new Rectangle(contentLeft, cardRect.Top + 86, contentWidth, 17),
            9f,
            FontStyle.Regular,
            _enabled ? DashboardPalette.DiscordText : DashboardPalette.Disabled,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

        var elapsed = DashboardTextFormatter.FormatElapsed(startedAt, DateTime.UtcNow);
        if (elapsed.Length == 0)
        {
            return;
        }

        DrawDiscordGameIcon(graphics, new Rectangle(contentLeft, cardRect.Top + 106, 14, 14));
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            elapsed,
            new Rectangle(contentLeft + 17, cardRect.Top + 104, contentWidth - 17, 18),
            9f,
            FontStyle.Regular,
            DashboardPalette.DiscordGreen);
    }

    private void DrawRoundedImage(Graphics graphics, Image image, Rectangle bounds, int radius)
    {
        using var path = DashboardDrawing.CreateRoundedPath(bounds, radius);
        var savedState = graphics.Save();
        try
        {
            graphics.SetClip(path);
            ImageAnimator.UpdateFrames(image);
            graphics.DrawImage(image, bounds);
        }
        catch
        {
            graphics.DrawImage(_fallbackImage, bounds);
        }
        finally
        {
            graphics.Restore(savedState);
        }
    }

    private void DrawCircularImage(Graphics graphics, Image image, Rectangle bounds)
    {
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        var savedState = graphics.Save();
        try
        {
            graphics.SetClip(path);
            ImageAnimator.UpdateFrames(image);
            graphics.DrawImage(image, bounds);
        }
        finally
        {
            graphics.Restore(savedState);
        }
    }

    private void DrawDiscordGameIcon(Graphics graphics, Rectangle bounds)
    {
        graphics.DrawImageUnscaled(_gameIcon, bounds.Location);
    }

    private void InvalidateIfAlive()
    {
        if (IsDisposed || Disposing)
        {
            return;
        }

        if (IsHandleCreated && InvokeRequired)
        {
            try
            {
                BeginInvoke(new MethodInvoker(InvalidateIfAlive));
            }
            catch (InvalidOperationException)
            {
            }

            return;
        }

        Invalidate();
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
        var statusColor = !_enabled ? DashboardPalette.Disabled : DashboardPalette.Accent;

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
        var valueWidth = Math.Max(140, ClientSize.Width - 100);
        DashboardDrawing.DrawValueRow(
            graphics,
            Font.FontFamily,
            new Rectangle(66, 218, valueWidth, 46),
            DashboardTextFormatter.FormatBillingType(usage?.BillingType));
        DashboardDrawing.DrawValueRow(
            graphics,
            Font.FontFamily,
            new Rectangle(66, 278, valueWidth, 46),
            DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit));
        DashboardDrawing.DrawValueRow(
            graphics,
            Font.FontFamily,
            new Rectangle(66, 338, valueWidth, 46),
            DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow));
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

        var activityColor = !_enabled ? DashboardPalette.Disabled : DashboardPalette.Accent;
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
