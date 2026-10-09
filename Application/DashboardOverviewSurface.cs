using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CodexDiscordPresence;

internal sealed class DashboardOverviewSurface : DashboardSurface
{
    private PresenceDashboardSnapshot _snapshot = PresenceDashboardSnapshot.Empty;
    private Image _ownerIcon = DashboardProviderPresentation.LoadIcon(null);
    private string? _iconProvider;
    private readonly ToolTip _toolTip = new();

    public DashboardOverviewSurface() => AccessibleName = "Presence owner and usage";

    public void SetSnapshot(PresenceDashboardSnapshot snapshot, bool enabled)
    {
        _snapshot = enabled ? snapshot : snapshot with { ProviderId = null, HasNoActiveProvider = true, TokenUsage = null };
        var provider = _snapshot.HasNoActiveProvider ? null : _snapshot.ProviderId;
        if (_iconProvider != provider)
        {
            _ownerIcon.Dispose();
            _ownerIcon = DashboardProviderPresentation.LoadIcon(provider);
            _iconProvider = provider;
        }
        _toolTip.SetToolTip(this, snapshot.ProjectName ?? string.Empty);
        var metrics = DashboardTextFormatter.CreateMetrics(_snapshot, DateTime.UtcNow);
        var usageDescription = metrics.Length == 0
            ? DashboardTextFormatter.FormatUsageNote(_snapshot)
            : string.Join(", ", metrics.Select(metric => metric.Label + " " + metric.Value));
        AccessibleDescription = DashboardTextFormatter.FormatProviderName(provider) + ", " + snapshot.ProjectName + ". " + usageDescription;
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _ownerIcon.Dispose(); _toolTip.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void DrawSurface(Graphics g, Size size)
    {
        g.InterpolationMode = InterpolationMode.NearestNeighbor;
        if (_iconProvider is not null) g.DrawImage(_ownerIcon, new Rectangle(0, 2, 20, 20));
        var name = DashboardTextFormatter.FormatProviderName(_iconProvider);
        using var font = new Font(Font.FontFamily, 13, FontStyle.Bold, GraphicsUnit.Pixel);
        var nameWidth = Math.Min(size.Width - 28, (int)Math.Ceiling(g.MeasureString(name, font).Width));
        DashboardDrawing.DrawText(g, Font.FontFamily, name, new Rectangle(28, 0, nameWidth, 24), 9.75f,
            FontStyle.Bold, DashboardPalette.Text);
        DashboardDrawing.DrawText(g, Font.FontFamily, string.IsNullOrWhiteSpace(_snapshot.ProjectName) ? null : "/  " + _snapshot.ProjectName,
            new Rectangle(28 + nameWidth + 8, 0, size.Width - nameWidth - 36, 24), 9f, FontStyle.Regular, DashboardPalette.MutedText);
        var metrics = DashboardTextFormatter.CreateMetrics(_snapshot, DateTime.UtcNow);
        if (metrics.Length == 0)
        {
            DashboardDrawing.DrawText(g, Font.FontFamily, DashboardTextFormatter.FormatUsageNote(_snapshot),
                new Rectangle(0, 30, size.Width, 44), 8.25f, FontStyle.Regular, DashboardPalette.SubtleText);
            return;
        }
        var width = (size.Width - 12) / 3;
        for (var i = 0; i < metrics.Length; i++) DrawTile(g, new Rectangle(i * (width + 6), 30, width, 44), metrics[i]);
    }

    private void DrawTile(Graphics g, Rectangle bounds, DashboardMetric metric)
    {
        DashboardDrawing.DrawRoundedSurface(g, bounds, 8, DashboardPalette.Surface, Color.FromArgb(35, 37, 43));
        DashboardDrawing.DrawText(g, Font.FontFamily, metric.Label, new Rectangle(bounds.X + 10, bounds.Y + 4, bounds.Width - 20, 14),
            8.25f, FontStyle.Regular, DashboardPalette.SubtleText);
        DashboardDrawing.DrawText(g, Font.FontFamily, metric.Value, new Rectangle(bounds.X + 10, bounds.Y + 18, bounds.Width - 20, 18),
            9.75f, FontStyle.Bold, DashboardPalette.Text);
        if (metric.ProgressPercent is not { } progress || !double.IsFinite(progress)) return;
        var track = new Rectangle(bounds.X + 10, bounds.Bottom - 6, bounds.Width - 20, 3);
        using var background = new SolidBrush(Color.FromArgb(42, 45, 53));
        using var fill = new SolidBrush(metric.Accent);
        g.FillRectangle(background, track);
        g.FillRectangle(fill, track.X, track.Y, (int)Math.Round(track.Width * Math.Clamp(progress, 0, 100) / 100), track.Height);
    }
}
