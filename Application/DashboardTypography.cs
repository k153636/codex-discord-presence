using System.Drawing;
using System.Drawing.Text;

namespace CodexDiscordPresence;

internal static class DashboardTypography
{
    private static readonly string[] DiscordFontCandidates =
    [
        "gg sans",
        "Segoe UI Variable Text",
        "Segoe UI"
    ];

    public static string DiscordFontFamilyName { get; } = ResolveDiscordFontFamilyName();

    public static FontFamily CreateDiscordFontFamily()
    {
        return new FontFamily(DiscordFontFamilyName);
    }

    private static string ResolveDiscordFontFamilyName()
    {
        try
        {
            using var installedFonts = new InstalledFontCollection();
            var installedNames = installedFonts.Families
                .Select(family => family.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            return DiscordFontCandidates.FirstOrDefault(installedNames.Contains) ?? "Segoe UI";
        }
        catch
        {
            return "Segoe UI";
        }
    }
}
