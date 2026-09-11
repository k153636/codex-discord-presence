using System.Text;

namespace CodexDiscordPresence;

internal static class DiscordPresencePayloadPolicy
{
    internal const int MaxTextBytes = 128;
    internal const int MaxButtonLabelBytes = 31;
    internal const int MaxButtonUrlLength = 512;

    public static string NormalizeText(string? value, int maxBytes = MaxTextBytes)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var oneLine = value
            .Replace('\r', ' ')
            .Replace('\n', ' ')
            .Replace('\t', ' ')
            .Trim();

        return TruncateUtf8(oneLine, maxBytes);
    }

    public static string? NormalizeOptionalText(string? value, int maxBytes = MaxTextBytes)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : NormalizeText(value, maxBytes);
    }

    public static bool TryNormalizeButton(RenderedButton button, out RenderedButton normalized)
    {
        var label = NormalizeText(button.Label, MaxButtonLabelBytes);
        var url = button.Url.Trim();
        if (label.Length == 0 ||
            url.Length == 0 ||
            url.Length > MaxButtonUrlLength ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsedUrl) ||
            parsedUrl.Scheme is not ("http" or "https"))
        {
            normalized = new RenderedButton(string.Empty, string.Empty);
            return false;
        }

        normalized = new RenderedButton(label, url);
        return true;
    }

    private static string TruncateUtf8(string value, int maxBytes)
    {
        if (maxBytes <= 0)
        {
            return string.Empty;
        }

        if (Encoding.UTF8.GetByteCount(value) <= maxBytes)
        {
            return value;
        }

        const string ellipsis = "…";
        var ellipsisBytes = Encoding.UTF8.GetByteCount(ellipsis);
        var contentBytes = Math.Max(0, maxBytes - ellipsisBytes);
        var builder = new StringBuilder();
        var usedBytes = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            var runeText = rune.ToString();
            var runeBytes = Encoding.UTF8.GetByteCount(runeText);
            if (usedBytes + runeBytes > contentBytes)
            {
                break;
            }

            builder.Append(runeText);
            usedBytes += runeBytes;
        }

        if (ellipsisBytes <= maxBytes)
        {
            builder.Append(ellipsis);
        }

        return builder.ToString();
    }
}
