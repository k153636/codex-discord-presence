using System.Drawing;
using System.Drawing.Text;

namespace CodexDiscordPresence;

internal static class DashboardTypography
{
    private const string OfficialDiscordFontFamilyName = "gg sans";
    private const string VariableTextFontFamilyName = "Segoe UI Variable Text";
    private const string VariableTextSemiboldFontFamilyName = "Segoe UI Variable Text Semibold";

    private static readonly string[] DiscordFontCandidates =
    [
        OfficialDiscordFontFamilyName,
        VariableTextFontFamilyName,
        "Segoe UI"
    ];

    public static string DiscordFontFamilyName { get; } = ResolveDiscordFontFamilyName();

    public static string DiscordSemiboldFontFamilyName { get; } = ResolveDiscordSemiboldFontFamilyName();

    public static FontStyle DiscordSemiboldFontStyle { get; } = ResolveDiscordSemiboldFontStyle();

    public static bool IsOfficialDiscordFontAvailable =>
        string.Equals(DiscordFontFamilyName, OfficialDiscordFontFamilyName, StringComparison.OrdinalIgnoreCase);

    public static FontFamily CreateDiscordFontFamily(bool semibold = false)
    {
        return new FontFamily(semibold ? DiscordSemiboldFontFamilyName : DiscordFontFamilyName);
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

    private static string ResolveDiscordSemiboldFontFamilyName()
    {
        try
        {
            using var installedFonts = new InstalledFontCollection();
            var installedNames = installedFonts.Families
                .Select(family => family.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (string.Equals(DiscordFontFamilyName, VariableTextFontFamilyName, StringComparison.OrdinalIgnoreCase) &&
                installedNames.Contains(VariableTextSemiboldFontFamilyName))
            {
                return VariableTextSemiboldFontFamilyName;
            }

            return DiscordFontFamilyName;
        }
        catch
        {
            return DiscordFontFamilyName;
        }
    }

    private static FontStyle ResolveDiscordSemiboldFontStyle()
    {
        return string.Equals(DiscordSemiboldFontFamilyName, DiscordFontFamilyName, StringComparison.OrdinalIgnoreCase)
            ? FontStyle.Bold
            : FontStyle.Regular;
    }
}
