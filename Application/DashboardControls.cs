using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal static class DashboardPalette
{
    public static readonly Color Window = Color.FromArgb(11, 12, 14);
    public static readonly Color SurfaceInset = Color.FromArgb(9, 10, 12);
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

internal readonly record struct DashboardMetric(
    string Label,
    string Value,
    Color Accent,
    double? ProgressPercent = null,
    bool IsPill = false);

internal static class DashboardLayoutMetrics
{
    public const int PreferredContentInset = 16;
    public const int ContentMaxWidth = 1120;
    public const int PreviewCardWidth = 360;

    public static int GetHorizontalInset(int clientWidth)
    {
        var availableInset = Math.Max(0, (clientWidth - PreviewCardWidth) / 2);
        return Math.Min(PreferredContentInset, availableInset);
    }

    public static int GetContentLeft(int clientWidth)
    {
        var inset = GetHorizontalInset(clientWidth);
        var availableWidth = Math.Max(1, clientWidth - inset * 2);
        var contentWidth = Math.Min(ContentMaxWidth, availableWidth);
        return inset + Math.Max(0, (availableWidth - contentWidth) / 2);
    }

    public static int GetContentWidth(int clientWidth)
    {
        var inset = GetHorizontalInset(clientWidth);
        return Math.Min(ContentMaxWidth, Math.Max(1, clientWidth - inset * 2));
    }
}

internal static class DashboardDrawing
{
    private const int StatusIndicatorRingPadding = 4;
    private const int VerticalMetricLayoutThreshold = 44;
    private const int MetricProgressHeight = 3;
    private const int MetricProgressBottomPadding = 12;
    private const int MetricValuePillHorizontalPadding = 10;

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

    public static void DrawText(
        Graphics graphics,
        FontFamily fontFamily,
        string? value,
        Rectangle bounds,
        float size,
        FontStyle style,
        Color color,
        TextFormatFlags flags = TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter)
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
        DashboardMetric metric)
    {
        using var accentBrush = new SolidBrush(metric.Accent);
        graphics.FillRectangle(accentBrush, bounds.Left, bounds.Top + 8, 3, Math.Max(1, bounds.Height - 16));

        if (bounds.Height >= VerticalMetricLayoutThreshold)
        {
            DrawText(
                graphics,
                fontFamily,
                metric.Label,
                new Rectangle(bounds.Left + 18, bounds.Top + 7, bounds.Width - 36, 14),
                9f,
                FontStyle.Regular,
                DashboardPalette.SubtleText,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            DrawMetricValue(
                graphics,
                fontFamily,
                new Rectangle(bounds.Left + 18, bounds.Top + 23, bounds.Width - 36, 20),
                metric);
            DrawMetricProgress(graphics, bounds, metric.ProgressPercent, metric.Accent);
            return;
        }

        DrawText(
            graphics,
            fontFamily,
            metric.Label,
            new Rectangle(bounds.Left + 18, bounds.Top + 9, Math.Min(98, bounds.Width / 3), bounds.Height - 18),
            12f,
            FontStyle.Regular,
            DashboardPalette.SubtleText,
            TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        DrawMetricValue(
            graphics,
            fontFamily,
            new Rectangle(bounds.Left + Math.Min(116, bounds.Width / 3 + 16), bounds.Top + 9, bounds.Width - Math.Min(134, bounds.Width / 3 + 34), bounds.Height - 18),
            metric,
            14f);
    }

    private static void DrawMetricValue(
        Graphics graphics,
        FontFamily fontFamily,
        Rectangle bounds,
        DashboardMetric metric,
        float fontSize = 12f)
    {
        if (!metric.IsPill)
        {
            DrawText(
                graphics,
                fontFamily,
                metric.Value,
                bounds,
                fontSize,
                FontStyle.Regular,
                DashboardPalette.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
            return;
        }

        using var font = new Font(fontFamily, fontSize, FontStyle.Regular);
        var measured = TextRenderer.MeasureText(
            graphics,
            metric.Value,
            font,
            new Size(int.MaxValue, bounds.Height),
            TextFormatFlags.NoPadding);
        var pillWidth = Math.Min(bounds.Width, Math.Max(1, measured.Width + MetricValuePillHorizontalPadding * 2));
        var pillHeight = Math.Min(22, bounds.Height);
        var pillBounds = new Rectangle(bounds.Left, bounds.Top + Math.Max(0, (bounds.Height - pillHeight) / 2), pillWidth, pillHeight);
        DrawRoundedSurface(graphics, pillBounds, pillHeight / 2, DashboardPalette.AccentSoft, DashboardPalette.Accent);
        DrawText(
            graphics,
            fontFamily,
            metric.Value,
            new Rectangle(pillBounds.Left + MetricValuePillHorizontalPadding, pillBounds.Top, Math.Max(1, pillBounds.Width - MetricValuePillHorizontalPadding * 2), pillBounds.Height),
            fontSize,
            FontStyle.Regular,
            DashboardPalette.Text,
            TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }

    private static void DrawMetricProgress(
        Graphics graphics,
        Rectangle bounds,
        double? progressPercent,
        Color accent)
    {
        if (!progressPercent.HasValue || bounds.Height < 54 || !double.IsFinite(progressPercent.Value))
        {
            return;
        }

        var track = new Rectangle(
            bounds.Left + 18,
            bounds.Bottom - MetricProgressBottomPadding,
            Math.Max(1, bounds.Width - 36),
            MetricProgressHeight);
        using var trackBrush = new SolidBrush(Color.FromArgb(55, accent));
        graphics.FillRectangle(trackBrush, track);

        var ratio = Math.Clamp(progressPercent.Value, 0d, 100d) / 100d;
        var fillWidth = (int)Math.Round(track.Width * ratio);
        if (fillWidth <= 0)
        {
            return;
        }

        using var fillBrush = new SolidBrush(accent);
        graphics.FillRectangle(fillBrush, track.Left, track.Top, fillWidth, track.Height);
    }
}

internal sealed class DashboardPreviewSurface : Control
{
    private const int DiscordCardWidth = DashboardLayoutMetrics.PreviewCardWidth;
    private const int DiscordCardHeight = 148;
    private const int ActivityTypeTextHeight = 22;
    private const int MenuTextHeight = 22;
    private const int ApplicationTextHeight = 21;
    private const int DetailTextHeight = 18;
    private const int StateTextHeight = 18;
    private const int ElapsedTextHeight = 18;

    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;
    private readonly Image _fallbackImage;
    private readonly Image _gameIcon;
    private readonly FontFamily _discordFontFamily;
    private readonly FontFamily _discordSemiboldFontFamily;
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
        BackColor = DashboardPalette.SurfaceInset;
        _fallbackImage = LoadCodexImage();
        _gameIcon = DashboardDiscordActivityIcon.Load();
        _discordFontFamily = DashboardTypography.CreateDiscordFontFamily();
        _discordSemiboldFontFamily = DashboardTypography.CreateDiscordFontFamily(semibold: true);
        _largeImage = new DashboardPresenceImageSlot(_fallbackImage, InvalidateIfAlive);
        _smallImage = new DashboardPresenceImageSlot(_fallbackImage, InvalidateIfAlive);
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        var publishedPresence = enabled ? snapshot.PublishedPresence : null;
        _largeImage.SetReference(publishedPresence?.LargeImageKey);
        _smallImage.SetReference(publishedPresence?.SmallImageKey);

        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _largeImage.Dispose();
            _smallImage.Dispose();
            _discordSemiboldFontFamily.Dispose();
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

        var cardRect = CreateCardBounds();
        DrawDiscordActivityCard(graphics, cardRect);
    }

    private Rectangle CreateCardBounds()
    {
        var left = DashboardLayoutMetrics.GetContentLeft(ClientSize.Width);
        var rightInset = DashboardLayoutMetrics.GetHorizontalInset(ClientSize.Width);
        var width = Math.Min(
            DiscordCardWidth,
            Math.Max(1, ClientSize.Width - left - rightInset));
        var height = width == DiscordCardWidth
            ? DiscordCardHeight
            : Math.Max(1, (int)Math.Round(width * (double)DiscordCardHeight / DiscordCardWidth));
        var top = Math.Max(0, (ClientSize.Height - height) / 2);
        return new Rectangle(left, top, width, height);
    }

    private void DrawDiscordActivityCard(Graphics graphics, Rectangle cardRect)
    {
        var publishedPresence = _enabled ? _snapshot.PublishedPresence : null;
        var presence = _enabled ? _snapshot.Presence : null;
        var details = publishedPresence is not null
            ? publishedPresence.Details
            : presence?.Details;
        var state = publishedPresence is not null
            ? publishedPresence.State
            : presence?.State;
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
            _discordSemiboldFontFamily,
            DashboardTextFormatter.FormatActivityType(publishedPresence),
            new Rectangle(cardRect.Left + 12, cardRect.Top + 9, 160, ActivityTypeTextHeight),
            12f,
            DashboardTypography.DiscordSemiboldFontStyle,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            _discordSemiboldFontFamily,
            "...",
            new Rectangle(cardRect.Right - 38, cardRect.Top + 5, 30, MenuTextHeight),
            12f,
            DashboardTypography.DiscordSemiboldFontStyle,
            DashboardPalette.DiscordText,
            TextFormatFlags.NoPadding |
            TextFormatFlags.HorizontalCenter |
            TextFormatFlags.VerticalCenter);

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
            _discordSemiboldFontFamily,
            "Codex",
            new Rectangle(contentLeft, cardRect.Top + 48, contentWidth, ApplicationTextHeight),
            14f,
            DashboardTypography.DiscordSemiboldFontStyle,
            DashboardPalette.DiscordText);
        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
            details,
            new Rectangle(contentLeft, cardRect.Top + 69, contentWidth, DetailTextHeight),
            12f,
            FontStyle.Regular,
            DashboardPalette.DiscordText,
            TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter);
        DashboardDrawing.DrawText(
            graphics,
            _discordFontFamily,
            state,
            new Rectangle(contentLeft, cardRect.Top + 87, contentWidth, StateTextHeight),
            12f,
            FontStyle.Regular,
            _enabled ? DashboardPalette.DiscordText : DashboardPalette.Disabled,
            TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis |
            TextFormatFlags.VerticalCenter);

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
            new Rectangle(contentLeft + 17, cardRect.Top + 105, contentWidth - 17, ElapsedTextHeight),
            12f,
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

internal sealed class DashboardOverviewSurface : Control
{
    private const int ContentTop = 16;
    private const int ContentBottom = 16;
    private const int LayoutGap = 16;
    private const int CompactHeroHeight = 112;
    private const int MetricRowHeight = 56;
    private const int ActivityTypeTextHeight = 26;
    private const int ActivityStateTextTop = 48;
    private const int ActivityStateTextHeight = 48;

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
        BackColor = DashboardPalette.SurfaceInset;
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
        DrawVerticalLayout(graphics, content);
    }

    private Rectangle CreateContentBounds()
    {
        var width = DashboardLayoutMetrics.GetContentWidth(ClientSize.Width);
        var left = DashboardLayoutMetrics.GetContentLeft(ClientSize.Width);
        var height = Math.Max(0, ClientSize.Height - ContentTop - ContentBottom);
        return new Rectangle(left, ContentTop, width, height);
    }

    private void DrawVerticalLayout(Graphics graphics, Rectangle content)
    {
        var hero = new Rectangle(content.Left, content.Top, content.Width, CompactHeroHeight);
        DrawActivityHero(graphics, hero);

        var metrics = new Rectangle(
            content.Left,
            hero.Bottom + LayoutGap,
            content.Width,
            content.Height - hero.Height - LayoutGap);
        DrawMetricList(graphics, metrics);
    }

    private void DrawActivityHero(Graphics graphics, Rectangle bounds)
    {
        var activity = DashboardTextFormatter.FormatActivity(_snapshot, _enabled);
        if (string.IsNullOrWhiteSpace(activity))
        {
            return;
        }

        var activityType = DashboardTextFormatter.FormatActivityType(
            _enabled ? _snapshot.PublishedPresence : null);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            activityType,
            new Rectangle(bounds.Left + 26, bounds.Top + 20, bounds.Width - 52, ActivityTypeTextHeight),
            12f,
            FontStyle.Bold,
            DashboardPalette.MutedText);

        using var dividerPen = new Pen(DashboardPalette.Divider);
        graphics.DrawLine(
            dividerPen,
            bounds.Left,
            bounds.Bottom - 1,
            bounds.Right,
            bounds.Bottom - 1);

        var activityColor = !_enabled ? DashboardPalette.Disabled : DashboardPalette.Accent;
        var stateTop = ActivityStateTextTop;
        var stateCenterY = bounds.Top + stateTop + 18;
        DashboardDrawing.DrawStatusIndicator(
            graphics,
            new Point(bounds.Left + 40, stateCenterY),
            12,
            activityColor);
        DashboardDrawing.DrawText(
            graphics,
            Font.FontFamily,
            activity,
            new Rectangle(bounds.Left + 64, bounds.Top + stateTop, bounds.Width - 90, ActivityStateTextHeight),
            24f,
            FontStyle.Bold,
            DashboardPalette.Text);
    }

    private void DrawMetricList(Graphics graphics, Rectangle bounds)
    {
        var metrics = CreateMetrics();
        if (metrics.Length == 0)
        {
            return;
        }

        var gap = LayoutGap;
        var cardWidth = bounds.Width;
        var availableCardHeight = (bounds.Height - gap * (metrics.Length - 1)) / metrics.Length;
        var cardHeight = Math.Min(MetricRowHeight, availableCardHeight);

        if (cardWidth <= 0 || cardHeight <= 0)
        {
            return;
        }

        for (var index = 0; index < metrics.Length; index++)
        {
            var cardBounds = new Rectangle(
                bounds.Left,
                bounds.Top + index * (cardHeight + gap),
                cardWidth,
                cardHeight);
            var metric = metrics[index];
            DashboardDrawing.DrawMetricCard(
                graphics,
                Font.FontFamily,
                cardBounds,
                metric);
        }
    }

    private DashboardMetric[] CreateMetrics()
    {
        var usage = _snapshot.TokenUsage;
        var metrics = new List<DashboardMetric>
        {
            new(
                "Discord",
                _snapshot.IsDiscordConnected ? "Connected" : "Disconnected",
                _snapshot.IsDiscordConnected ? DashboardPalette.Green : DashboardPalette.Accent)
        };

        var billing = DashboardTextFormatter.FormatBillingType(usage?.BillingType);
        if (!string.IsNullOrWhiteSpace(billing))
        {
            metrics.Add(new DashboardMetric("Billing", billing, DashboardPalette.Accent, IsPill: true));
        }

        var rateLimit = usage?.RateLimit;
        var usageText = DashboardTextFormatter.FormatRateLimitUsage(rateLimit);
        if (!string.IsNullOrWhiteSpace(usageText))
        {
            metrics.Add(new DashboardMetric("Usage", usageText, DashboardPalette.Green, rateLimit?.UsedPercent));
        }

        var resetText = DashboardTextFormatter.FormatRateLimitReset(rateLimit, DateTime.UtcNow);
        if (!string.IsNullOrWhiteSpace(resetText))
        {
            metrics.Add(new DashboardMetric(
                "Reset",
                resetText.Replace("reset ", "", StringComparison.Ordinal),
                DashboardPalette.Accent));
        }

        return metrics.ToArray();
    }
}
