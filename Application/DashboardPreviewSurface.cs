using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal sealed class DashboardPreviewSurface : DashboardSurface
{
    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private bool _enabled = true;
    private readonly Image _neutralImage = SystemIcons.Application.ToBitmap();
    private readonly Image _gameIcon = DashboardDiscordActivityIcon.Load();
    private readonly FontFamily _discordFontFamily = DashboardTypography.CreateDiscordFontFamily();
    private readonly DashboardPresenceImageSlot _largeImage;
    private readonly DashboardPresenceImageSlot _smallImage;
    private readonly DashboardConnectionPill _connection = new();

    public DashboardPreviewSurface()
    {
        _largeImage = new(_neutralImage, InvalidateIfAlive);
        _smallImage = new(_neutralImage, InvalidateIfAlive);
        Controls.Add(_connection);
        AccessibleName = "RPC Preview";
    }

    public static int GetPreferredHeight(PresenceDashboardSnapshot snapshot) => snapshot.PublishedPresence?.Buttons.Count > 0 ? 212 : 178;

    internal event Action<PresenceDashboardSnapshot>? PresenceDisplayed;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_enabled)
        {
            PresenceDisplayed?.Invoke(_snapshot);
        }
    }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = snapshot;
        _enabled = enabled;
        _largeImage.SetReference(snapshot.PublishedPresence?.LargeImageKey);
        _smallImage.SetReference(snapshot.PublishedPresence?.SmallImageKey);
        _connection.SetSnapshot(snapshot);
        var empty = DashboardTextFormatter.FormatEmptyPreview(snapshot, enabled);
        AccessibleDescription = snapshot.PublishedPresence is { } presence
            ? $"Last acknowledged Discord presence. {DashboardTextFormatter.FormatProviderName(snapshot.ProviderId)}. {presence.Details}. {presence.State}. {DashboardTextFormatter.FormatConnection(snapshot)}."
            : empty.Title + ". " + empty.Description;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_connection is null) return;
        var scale = DeviceDpi / 96f;
        _connection.Bounds = Rectangle.Round(new RectangleF(92 * scale, scale, 122 * scale, 20 * scale));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _largeImage.Dispose(); _smallImage.Dispose(); _neutralImage.Dispose();
            _gameIcon.Dispose(); _discordFontFamily.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void DrawSurface(Graphics g, Size size)
    {
        DashboardDrawing.DrawText(g, Font.FontFamily, "RPC Preview", new Rectangle(0, 0, 90, 22), 9f,
            FontStyle.Bold, DashboardPalette.Text);
        var card = new Rectangle(0, 30, size.Width - 1, GetPreferredHeight(_snapshot) - 31);
        using var path = DashboardDrawing.CreateRoundedPath(card, 12);
        using var gradient = new LinearGradientBrush(card, DashboardPalette.DiscordCardTop, DashboardPalette.DiscordCardBottom, LinearGradientMode.Vertical);
        g.FillPath(gradient, path);
        var presence = _snapshot.PublishedPresence;
        if (presence is null)
        {
            var empty = DashboardTextFormatter.FormatEmptyPreview(_snapshot, _enabled);
            DashboardDrawing.DrawText(g, _discordFontFamily, empty.Title, new Rectangle(12, 68, card.Width - 24, 30),
                10.5f, FontStyle.Bold, DashboardPalette.DiscordText);
            DashboardDrawing.DrawText(g, _discordFontFamily, empty.Description, new Rectangle(12, 100, card.Width - 24, 32),
                9f, FontStyle.Regular, DashboardPalette.DiscordText);
            return;
        }
        var color = DashboardPalette.DiscordText;
        DashboardDrawing.DrawText(g, _discordFontFamily, DashboardTextFormatter.FormatActivityType(presence),
            new Rectangle(12, card.Top + 9, card.Width - 24, 20), 9f, FontStyle.Bold, color);
        if (!string.IsNullOrWhiteSpace(presence.LargeImageKey)) DrawRoundedImage(g, _largeImage.CurrentImage, new Rectangle(12, card.Top + 40, 88, 88));
        if (!string.IsNullOrWhiteSpace(presence.SmallImageKey)) DrawSmallImage(g, new Rectangle(76, card.Top + 104, 28, 28));
        var textLeft = 112;
        var textWidth = card.Width - textLeft - 12;
        DashboardDrawing.DrawText(g, _discordFontFamily, DashboardTextFormatter.FormatProviderName(_snapshot.ProviderId),
            new Rectangle(textLeft, card.Top + 40, textWidth, 20), 10.5f, FontStyle.Bold, DashboardPalette.Text);
        DashboardDrawing.DrawText(g, _discordFontFamily, presence.Details, new Rectangle(textLeft, card.Top + 63, textWidth, 18),
            9f, FontStyle.Regular, color);
        DashboardDrawing.DrawText(g, _discordFontFamily, presence.State, new Rectangle(textLeft, card.Top + 82, textWidth, 18),
            9f, FontStyle.Regular, color);
        var elapsed = DashboardTextFormatter.FormatElapsed(presence.StartedAtUtc, DateTime.UtcNow);
        if (elapsed.Length > 0)
        {
            g.DrawImage(_gameIcon, new Rectangle(textLeft, card.Top + 105, 14, 14));
            DashboardDrawing.DrawText(g, _discordFontFamily, elapsed, new Rectangle(textLeft + 18, card.Top + 101, textWidth - 18, 22),
                9f, FontStyle.Regular, DashboardPalette.DiscordGreen);
        }
        if (presence.Buttons.FirstOrDefault() is { } button)
        {
            var buttonBounds = new Rectangle(12, card.Top + 138, card.Width - 24, 32);
            DashboardDrawing.DrawRoundedSurface(g, buttonBounds, 6, Color.FromArgb(78, 80, 88), Color.Transparent);
            DashboardDrawing.DrawText(g, _discordFontFamily, button.Label, Rectangle.Inflate(buttonBounds, -12, 0),
                9.75f, FontStyle.Bold, DashboardPalette.Text);
        }
        if (!_enabled || !_snapshot.IsDiscordConnected || _snapshot.HasNoActiveProvider)
        {
            using var dim = new SolidBrush(Color.FromArgb(105, DashboardPalette.Window));
            g.FillPath(dim, path);
        }
    }

    private static void DrawRoundedImage(Graphics g, Image image, Rectangle bounds)
    {
        using var path = DashboardDrawing.CreateRoundedPath(bounds, 8);
        var state = g.Save();
        try { g.SetClip(path); UpdateFrame(image); g.DrawImage(image, bounds); }
        finally { g.Restore(state); }
    }

    private void DrawSmallImage(Graphics g, Rectangle bounds)
    {
        using var ring = new SolidBrush(Color.FromArgb(31, 31, 33));
        g.FillEllipse(ring, Rectangle.Inflate(bounds, 3, 3));
        using var path = new GraphicsPath();
        path.AddEllipse(bounds);
        var state = g.Save();
        try { g.SetClip(path); UpdateFrame(_smallImage.CurrentImage); g.InterpolationMode = InterpolationMode.NearestNeighbor; g.DrawImage(_smallImage.CurrentImage, bounds); }
        finally { g.Restore(state); }
    }

    private static void UpdateFrame(Image image)
    {
        if (DashboardAnimationPreferences.AnimationsEnabled) ImageAnimator.UpdateFrames(image);
    }

    private void InvalidateIfAlive()
    {
        if (IsDisposed || Disposing) return;
        if (IsHandleCreated && InvokeRequired)
        {
            try { BeginInvoke(new MethodInvoker(InvalidateIfAlive)); }
            catch (InvalidOperationException) { }
            return;
        }
        Invalidate();
    }
}

internal sealed class DashboardConnectionPill : DashboardSurface
{
    private string _status = "Disconnected";
    public DashboardConnectionPill() { AccessibleName = "Discord Disconnected"; TabStop = false; }

    public void SetSnapshot(PresenceDashboardSnapshot snapshot)
    {
        var status = DashboardTextFormatter.FormatConnection(snapshot);
        if (_status == status) return;
        _status = status;
        AccessibleName = "Discord " + status;
        AccessibleDescription = status == "Disconnected" ? "Not publishing" : status;
        AccessibilityNotifyClients(AccessibleEvents.NameChange, -1);
        Invalidate();
    }

    protected override void DrawSurface(Graphics g, Size size)
    {
        var color = _status switch { "Connected" => DashboardPalette.Green, "Connecting" => DashboardPalette.Amber, _ => DashboardPalette.Red };
        var width = _status == "Disconnected" ? 118 : _status == "Connecting" ? 109 : 105;
        DashboardDrawing.DrawRoundedSurface(g, new Rectangle(0, 0, Math.Min(width, size.Width - 1), 19), 10,
            Color.FromArgb(30, color), Color.FromArgb(95, color));
        using var pen = new Pen(color, 1.5f);
        using var brush = new SolidBrush(color);
        if (_status == "Connected") g.FillEllipse(brush, 8, 6, 8, 8);
        else { g.DrawEllipse(pen, 8, 6, 8, 8); if (_status == "Connecting") g.FillEllipse(brush, 11, 9, 2, 2); }
        DashboardDrawing.DrawText(g, Font.FontFamily, _status, new Rectangle(21, 0, width - 27, 20),
            8.25f, FontStyle.Bold, color);
    }
}
