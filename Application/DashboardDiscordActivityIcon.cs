using System.Drawing.Imaging;

namespace CodexDiscordPresence;

internal static class DashboardDiscordActivityIcon
{
    // Measured 14x14 Discord desktop activity-card gamepad glyph at 100% scale.
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAABHSURBVDhPY2AYcFB3cOJ/fBhdPRigK8KF0fUR1AhTg64PRRBdE7oBKABdAbpNWDWiKyaEqa8RLkGMPE4JAnJggFOCgBztAQBfQuvtefWBdQAAAABJRU5ErkJggg==";

    public static Image Load()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(PngBase64));
        using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(source);
    }
}
