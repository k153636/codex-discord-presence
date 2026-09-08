using Xunit;

namespace CodexDiscordPresence.Tests;

public sealed class DashboardTypographyTests
{
    [Fact]
    public void DiscordFontFamilyName_UsesAnInstalledDiscordCompatibleFamily()
    {
        IEnumerable<string> supportedFamilies = ["gg sans", "Segoe UI Variable Text", "Segoe UI"];

        Assert.Contains(DashboardTypography.DiscordFontFamilyName, supportedFamilies);
    }

    [Fact]
    public void DiscordSemiboldFont_ResolvesAnAvailableSemiboldStyle()
    {
        using var fontFamily = DashboardTypography.CreateDiscordFontFamily(semibold: true);

        Assert.True(fontFamily.IsStyleAvailable(DashboardTypography.DiscordSemiboldFontStyle));
    }
}
