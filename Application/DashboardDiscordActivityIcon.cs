using System.Drawing.Imaging;

namespace CodexDiscordPresence;

internal static class DashboardDiscordActivityIcon
{
    // 14x14 Discord desktop activity-card gamepad glyph, preserving the rendered
    // dark-green edge pixels visible in the reference card.
    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAA4AAAAOCAYAAAAfSC3RAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAD0SURBVDhPY/j//z8DORhDgFgMZxg5Wf7PWVzzv2Zv///o1oz/fgWRYBzdlg4WA8kZO1n8x9CYs7j2f93BiXgxSDOKRmMnS7BEeH0yGKNriOvO+W9gYwpmmzhZgjWDNTpEef6PakmHmwZiI2vKnFsJpkF8hwhPhEbv7FCwYEBJNBgj2wayCaYJhD3TgxAaffPCMZyHC/vmRSBpzI9AkUzoz/8fWpP4P7gy/n/G7DIUOVBIY9VYvaf3v7KGOty/KpoaYDGsGu1C3eASID/CNMFwcHkcXN4+zA2hEYRBIeuTG46hCYb98iP+O8V4YyYAUjGGALEYAD1GFCfC8ocDAAAAAElFTkSuQmCC";

    public static Image Load()
    {
        using var stream = new MemoryStream(Convert.FromBase64String(PngBase64));
        using var source = Image.FromStream(stream, useEmbeddedColorManagement: false, validateImageData: true);
        return new Bitmap(source);
    }
}
