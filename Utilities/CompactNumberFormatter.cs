using System.Globalization;

namespace CodexDiscordPresence;

internal static class CompactNumberFormatter
{
    private static readonly string[] Suffixes = ["", "K", "M", "B", "T"];

    public static string Format(long value)
    {
        var sign = value < 0 ? "-" : "";
        var magnitude = Math.Abs((decimal)value);
        var suffixIndex = 0;

        while (magnitude >= 1_000m && suffixIndex < Suffixes.Length - 1)
        {
            magnitude /= 1_000m;
            suffixIndex++;
        }

        var roundedMagnitude = Math.Round(magnitude, 1, MidpointRounding.AwayFromZero);
        if (roundedMagnitude >= 1_000m && suffixIndex < Suffixes.Length - 1)
        {
            roundedMagnitude = Math.Round(roundedMagnitude / 1_000m, 1, MidpointRounding.AwayFromZero);
            suffixIndex++;
        }

        return $"{sign}{roundedMagnitude.ToString("0.#", CultureInfo.InvariantCulture)}{Suffixes[suffixIndex]}";
    }
}
