using System.Drawing;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardDiscordActivityIconTests
{
    [Fact]
    public void Load_ReturnsTheDiscordActivityGlyphWithItsPixelEdgeAndCutouts()
    {
        using var icon = DashboardDiscordActivityIcon.Load();
        using var bitmap = new Bitmap(icon);

        Assert.Equal(14, bitmap.Width);
        Assert.Equal(14, bitmap.Height);
        Assert.Equal(0, bitmap.GetPixel(0, 0).A);
        Assert.True(bitmap.GetPixel(2, 2).A > 0);
        Assert.True(bitmap.GetPixel(2, 2).G < bitmap.GetPixel(7, 4).G);
        Assert.Equal(0, bitmap.GetPixel(4, 5).A);
        Assert.Equal(0, bitmap.GetPixel(7, 10).A);
    }
}
