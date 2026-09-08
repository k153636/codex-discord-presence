using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Forms;
using DiscordRPC;

namespace CodexDiscordPresence.PreviewCapture;

internal static class Program
{
    private const int PreviewCardWidth = 360;
    private const int PreviewCardHeight = 148;

    [STAThread]
    private static int Main(string[] args)
    {
        var outputPath = args.Length == 0
            ? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "Preview", "rpc-preview-test.png"))
            : Path.GetFullPath(args[0]);

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        var runtimeState = new PresenceRuntimeState();
        runtimeState.PublishDashboardSnapshot(CreateShowcaseSnapshot());

        using var form = new CodexDashboardForm(runtimeState);
        form.Show();
        PumpMessages(TimeSpan.FromMilliseconds(500));

        var overviewLayout = form.Controls.OfType<TableLayoutPanel>().Single();
        var previewSurface = overviewLayout.GetControlFromPosition(0, 1)
            ?? throw new InvalidOperationException("The dashboard preview surface was not found.");

        var previewBounds = new Rectangle(Point.Empty, previewSurface.ClientSize);
        if (previewBounds.Width < PreviewCardWidth || previewBounds.Height < PreviewCardHeight)
        {
            throw new InvalidOperationException(
                $"Preview surface is too small: {previewBounds.Width}x{previewBounds.Height}.");
        }

        using var surfaceBitmap = new Bitmap(
            previewBounds.Width,
            previewBounds.Height,
            PixelFormat.Format32bppArgb);
        previewSurface.DrawToBitmap(surfaceBitmap, previewBounds);

        var crop = new Rectangle(
            (previewBounds.Width - PreviewCardWidth) / 2,
            (previewBounds.Height - PreviewCardHeight) / 2,
            PreviewCardWidth,
            PreviewCardHeight);
        using var cardBitmap = surfaceBitmap.Clone(crop, PixelFormat.Format32bppArgb);
        cardBitmap.Save(outputPath, ImageFormat.Png);

        form.Close();
        return 0;
    }

    private static PresenceDashboardSnapshot CreateShowcaseSnapshot()
    {
        // These values mirror the sanitized, real Presence rendered record used
        // by the current showcase. Keep the fixture free of raw session data.
        var startedAtUtc = DateTime.UtcNow.AddMinutes(-2);
        var publishedPresence = new DiscordPresenceSnapshot(
            "gpt 5.6 luna max 1.5x • 125M Token",
            "MCP chrome-devtools",
            "rpc_reading",
            "MCP chrome-devtools",
            "rpc_codex",
            "Codex",
            startedAtUtc,
            5,
            5,
            [])
        {
            ActivityType = ActivityType.Playing
        };

        return new PresenceDashboardSnapshot(
            AppProfileKind.Codex,
            "gpt 5.6 luna",
            "codex-discord-RPC",
            null,
            null,
            true,
            DateTime.UtcNow)
        {
            PublishedPresence = publishedPresence
        };
    }

    private static void PumpMessages(TimeSpan duration)
    {
        var deadline = DateTime.UtcNow + duration;
        while (DateTime.UtcNow < deadline)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }
}
