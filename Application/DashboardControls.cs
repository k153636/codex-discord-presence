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
    private const int StatusIndicatorRingPadding = 4;
    private const int VerticalMetricLayoutThreshold = 76;

    public static void DrawStatusIndicator(
        Graphics graphics,
        Point center,
        int diameter,
        Color color)
    {
        var ringBounds = new Rectangle(
            center.X - diameter / 2 - StatusIndicatorRingPadding,
            center.Y - diameter / 2 - StatusIndicatorRingPadding,
            diameter + StatusIndicatorRingPadding * 2,
            diameter + StatusIndicatorRingPadding * 2);
        using var ring = new SolidBrush(Color.FromArgb(42, color));
        graphics.FillEllipse(ring, ringBounds);

        var dotBounds = new Rectangle(
            center.X - diameter / 2,
            center.Y - diameter / 2,
            diameter,
            diameter);
        using var dot = new SolidBrush(color);
        graphics.FillEllipse(dot, dotBounds);
    }

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

        if (bounds.Height >= VerticalMetricLayoutThreshold)
        {
            DrawText(
                graphics,
                fontFamily,
                label,
                new Rectangle(bounds.Left + 18, bounds.Top + 10, bounds.Width - 36, 16),
                10f,
                FontStyle.Regular,
                DashboardPalette.SubtleText,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            DrawText(
                graphics,
                fontFamily,
                value,
                new Rectangle(bounds.Left + 18, bounds.Top + 29, bounds.Width - 36, 22),
                14f,
                FontStyle.Regular,
                DashboardPalette.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            return;
        }

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
    private const int DiscordCardWidth = 360;
    private const int DiscordCardHeight = 148;
    private const int StageWidth = 420;
    private const int StageHeight = 220;
    private const int StageMargin = 24;
    private const int StageLabelTop = 18;
    private const int StageCardTop = 48;

    internal const string CurrentActivityLabel = "Current Activity";
    internal const string PlayingLabel = "Playing:";

    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;
    private readonly Image _fallbackImage;
    private readonly Image _gameIcon;
    private readonly FontFamily _discordFontFamily;
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
        _discordFontFamily = DashboardTypography.CreateDiscordFontFamily();
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
            _discordFontFamily.Dispose();
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

        var stage = CreateStageBounds();
        DashboardDrawing.DrawShadow(graphics, stage, 14);
        DashboardDrawing.DrawRoundedSurface(
            graphics,
            stage,
            14,
            DashboardPalette.Surface,
            DashboardPalette.Border);

        var cardLeft = stage.Left + (stage.Width - DiscordCardWidth) / 2;
        var cardRect = new Rectangle(cardLeft, stage.Top + StageCardTop, DiscordCardWidth, DiscordCardHeight);

        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
            CurrentActivityLabel,
            new Rectangle(cardRect.Left, stage.Top + StageLabelTop, DiscordCardWidth, 20),
            10f,
            FontStyle.Regular,
            DashboardPalette.DiscordText);
        DrawDiscordActivityCard(graphics, cardRect);
    }

    private Rectangle CreateStageBounds()
    {
        var width = Math.Min(StageWidth, Math.Max(392, ClientSize.Width - StageMargin * 2));
        var height = Math.Min(StageHeight, Math.Max(192, ClientSize.Height - StageMargin * 2));
        var left = Math.Max(StageMargin, (ClientSize.Width - width) / 2);
        var top = Math.Max(StageMargin, (ClientSize.Height - height) / 2);
        return new Rectangle(left, top, width, height);
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
            _discordFontFamily,
            PlayingLabel,
            new Rectangle(cardRect.Left + 12, cardRect.Top + 11, 160, 18),
            10f,
            FontStyle.Bold,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
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
            _discordFontFamily,
            "Codex",
            new Rectangle(contentLeft, cardRect.Top + 51, contentWidth, 18),
            11f,
            FontStyle.Bold,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
            details,
            new Rectangle(contentLeft, cardRect.Top + 69, contentWidth, 17),
            9f,
            FontStyle.Regular,
            DashboardPalette.DiscordText,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
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
            _discordFontFamily,
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
    private const int HorizontalInset = 30;
    private const int ContentLeft = 66;
    private const int MetricStartY = 174;
    private const int MetricGap = 62;

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

        var contentWidth = Math.Max(0, ClientSize.Width - ContentLeft - HorizontalInset);
        var activity = DashboardTextFormatter.FormatActivity(_snapshot, _enabled);
        var statusColor = !_enabled ? DashboardPalette.Disabled : DashboardPalette.Accent;
        DashboardDrawing.DrawStatusIndicator(graphics, new Point(42, 58), 12, statusColor);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            activity,
            new Rectangle(ContentLeft, 39, contentWidth, 30),
            16f,
            FontStyle.Bold,
            DashboardPalette.Text);

        var connection = !_enabled
            ? "Presence disabled"
            : _snapshot.IsDiscordConnected
                ? "Connected to Discord"
                : "Connecting to Discord";
        var connectionColor = !_enabled
            ? DashboardPalette.Disabled
            : _snapshot.IsDiscordConnected
                ? DashboardPalette.Green
                : DashboardPalette.Accent;
        DashboardDrawing.DrawStatusIndicator(graphics, new Point(42, 102), 6, connectionColor);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            connection,
            new Rectangle(ContentLeft, 83, contentWidth, 44),
            13f,
            FontStyle.Regular,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);

        using var sectionDivider = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(
            sectionDivider,
            HorizontalInset,
            148,
            Math.Max(HorizontalInset, ClientSize.Width - HorizontalInset),
            148);

        var usage = _snapshot.TokenUsage;
        DrawRailMetric(
            graphics,
            Font.FontFamily,
            new Rectangle(ContentLeft, MetricStartY, contentWidth, 48),
            "Billing",
            DashboardTextFormatter.FormatBillingType(usage?.BillingType),
            DashboardPalette.Accent);
        DrawRailMetric(
            graphics,
            Font.FontFamily,
            new Rectangle(ContentLeft, MetricStartY + MetricGap, contentWidth, 48),
            "Usage",
            DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit),
            DashboardPalette.Green);
        DrawRailMetric(
            graphics,
            Font.FontFamily,
            new Rectangle(ContentLeft, MetricStartY + MetricGap * 2, contentWidth, 48),
            "Reset",
            DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow),
            DashboardPalette.Accent);
    }

    private static void DrawRailMetric(
        Graphics graphics,
        FontFamily fontFamily,
        Rectangle bounds,
        string label,
        string value,
        Color accent)
    {
        using var accentBrush = new SolidBrush(accent);
        graphics.FillRectangle(accentBrush, bounds.Left - 32, bounds.Top + 6, 3, 36);
        DashboardDrawing.DrawText(
            graphics,
            fontFamily,
            label,
            new Rectangle(bounds.Left, bounds.Top, bounds.Width, 16),
            10f,
            FontStyle.Regular,
            DashboardPalette.SubtleText,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        DashboardDrawing.DrawText(
            graphics,
            fontFamily,
            value,
            new Rectangle(bounds.Left, bounds.Top + 17, bounds.Width, 24),
            13f,
            FontStyle.Regular,
            DashboardPalette.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class DashboardOverviewSurface : Control
{
    private const int ContentMargin = 40;
    private const int ContentTop = 32;
    private const int ContentMaxWidth = 1120;
    private const int LayoutGap = 16;
    private const int WideLayoutThreshold = 760;
    private const int WideHeroWidth = 520;
    private const int MinimumHeroWidth = 420;
    private const int WideHeroHeight = 230;
    private const int CompactHeroHeight = 184;
    private const int MetricGap = 14;

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

        var content = CreateContentBounds();
        if (content.Width >= WideLayoutThreshold)
        {
            DrawWideLayout(graphics, content);
        }
        else
        {
            DrawCompactLayout(graphics, content);
        }
    }

    private Rectangle CreateContentBounds()
    {
        var width = Math.Min(
            ContentMaxWidth,
            Math.Max(220, ClientSize.Width - ContentMargin * 2));
        var left = Math.Max(ContentMargin, (ClientSize.Width - width) / 2);
        var height = Math.Max(0, ClientSize.Height - ContentTop - ContentMargin);
        return new Rectangle(left, ContentTop, width, height);
    }

    private void DrawWideLayout(Graphics graphics, Rectangle content)
    {
        var heroWidth = Math.Min(
            WideHeroWidth,
            Math.Max(MinimumHeroWidth, content.Width - 360));
        var hero = new Rectangle(content.Left, content.Top, heroWidth, WideHeroHeight);
        DrawActivityHero(graphics, hero);

        var metrics = new Rectangle(
            hero.Right + LayoutGap,
            content.Top,
            content.Width - hero.Width - LayoutGap,
            WideHeroHeight);
        DrawMetricGrid(graphics, metrics, columns: 2);
    }

    private void DrawCompactLayout(Graphics graphics, Rectangle content)
    {
        var hero = new Rectangle(content.Left, content.Top, content.Width, CompactHeroHeight);
        DrawActivityHero(graphics, hero);

        var metrics = new Rectangle(
            content.Left,
            hero.Bottom + LayoutGap,
            content.Width,
            content.Height - hero.Height - LayoutGap);
        DrawMetricGrid(graphics, metrics, columns: content.Width >= 620 ? 2 : 1);
    }

    private void DrawActivityHero(Graphics graphics, Rectangle bounds)
    {
        DashboardDrawing.DrawShadow(graphics, bounds, 16);
        DashboardDrawing.DrawRoundedSurface(
            graphics,
            bounds,
            16,
            DashboardPalette.SurfaceRaised,
            DashboardPalette.Border);

        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            "Current Activity",
            new Rectangle(bounds.Left + 26, bounds.Top + 22, bounds.Width - 52, 24),
            12f,
            FontStyle.Bold,
            DashboardPalette.MutedText);

        var activityColor = !_enabled ? DashboardPalette.Disabled : DashboardPalette.Accent;
        var stateTop = bounds.Height >= WideHeroHeight ? 64 : 58;
        var stateCenterY = bounds.Top + stateTop + 18;
        DashboardDrawing.DrawStatusIndicator(
            graphics,
            new Point(bounds.Left + 40, stateCenterY),
            12,
            activityColor);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            DashboardTextFormatter.FormatActivity(_snapshot, _enabled),
            new Rectangle(bounds.Left + 64, bounds.Top + stateTop, bounds.Width - 90, 36),
            24f,
            FontStyle.Bold,
            DashboardPalette.Text);

        var dividerY = bounds.Top + (bounds.Height >= WideHeroHeight ? 116 : 106);
        using var divider = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(divider, bounds.Left + 28, dividerY, bounds.Right - 28, dividerY);
        var modelTop = dividerY + 14;
        var modelHeight = Math.Max(42, bounds.Bottom - modelTop - 18);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            DashboardTextFormatter.FormatModelProject(_snapshot),
            new Rectangle(bounds.Left + 28, modelTop, bounds.Width - 56, modelHeight),
            13f,
            FontStyle.Regular,
            DashboardPalette.MutedText,
            TextFormatFlags.NoPadding | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
    }

    private void DrawMetricGrid(Graphics graphics, Rectangle bounds, int columns)
    {
        var metrics = CreateMetrics();
        var safeColumns = Math.Max(1, Math.Min(columns, metrics.Length));
        var rows = (metrics.Length + safeColumns - 1) / safeColumns;
        var gap = safeColumns == 1 ? 12 : MetricGap;
        var cardWidth = (bounds.Width - gap * (safeColumns - 1)) / safeColumns;
        var cardHeight = (bounds.Height - gap * (rows - 1)) / rows;

        if (cardWidth <= 0 || cardHeight <= 0)
        {
            return;
        }

        for (var index = 0; index < metrics.Length; index++)
        {
            var column = index % safeColumns;
            var row = index / safeColumns;
            var cardBounds = new Rectangle(
                bounds.Left + column * (cardWidth + gap),
                bounds.Top + row * (cardHeight + gap),
                cardWidth,
                cardHeight);
            var metric = metrics[index];
            DashboardDrawing.DrawMetricCard(
                graphics,
                Font.FontFamily,
                cardBounds,
                metric.Label,
                metric.Value,
                metric.Accent);
        }
    }

    private (string Label, string Value, Color Accent)[] CreateMetrics()
    {
        var usage = _snapshot.TokenUsage;
        return
        [
            ("Discord", _snapshot.IsDiscordConnected ? "Connected" : "Disconnected", _snapshot.IsDiscordConnected ? DashboardPalette.Green : DashboardPalette.Accent),
            ("Billing", DashboardTextFormatter.FormatBillingType(usage?.BillingType), DashboardPalette.Accent),
            ("Usage", DashboardTextFormatter.FormatRateLimitUsage(usage?.RateLimit), DashboardPalette.Green),
            ("Reset", DashboardTextFormatter.FormatRateLimitReset(usage?.RateLimit, DateTime.UtcNow).Replace("reset ", "", StringComparison.Ordinal), DashboardPalette.Accent)
        ];
    }
}
