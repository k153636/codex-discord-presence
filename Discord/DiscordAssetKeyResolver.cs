namespace CodexDiscordPresence;

internal static class DiscordAssetKeyResolver
{
    public static string? ResolveLargeImageReference(DiscordOptions options, RenderedPresence presence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presence);

        return ResolveImageReference(options, ResolveLargeImageKey(options, presence));
    }

    public static string? ResolveImageReference(DiscordOptions options, string? imageKey)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!TryNormalize(imageKey, out var normalizedKey))
        {
            return null;
        }

        if (TryGetConfiguredExternalUrl(options.ExternalImageUrls, normalizedKey, out var externalUrl))
        {
            return externalUrl;
        }

        return normalizedKey;
    }

    public static string? ResolveLargeImageKey(DiscordOptions options, RenderedPresence presence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presence);

        if (presence.ActivityKind == CodexActivityKind.RunningCommand &&
            TryGetConfiguredKey(
                options.RunningCommandImageKeys,
                presence.RunningCommandKind.ToString(),
                out var commandImageKey))
        {
            return commandImageKey;
        }

        return TryGetConfiguredKey(
                options.ActivityImageKeys,
                presence.ActivityKind.ToString(),
                out var activityImageKey)
            ? activityImageKey
            : Normalize(options.LargeImageKey);
    }

    private static bool TryGetConfiguredKey(
        IReadOnlyDictionary<string, string>? mappings,
        string name,
        out string key)
    {
        if (mappings is not null)
        {
            if (mappings.TryGetValue(name, out var directValue) &&
                TryNormalize(directValue, out key))
            {
                return true;
            }

            foreach (var pair in mappings)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) &&
                    TryNormalize(pair.Value, out key))
                {
                    return true;
                }
            }
        }

        key = "";
        return false;
    }

    private static bool TryGetConfiguredExternalUrl(
        IReadOnlyDictionary<string, string>? mappings,
        string name,
        out string url)
    {
        if (TryGetConfiguredValue(mappings, name, out var configuredValue) &&
            Uri.TryCreate(configuredValue, UriKind.Absolute, out var candidate) &&
            (candidate.Scheme == Uri.UriSchemeHttp || candidate.Scheme == Uri.UriSchemeHttps))
        {
            url = candidate.AbsoluteUri;
            return true;
        }

        url = "";
        return false;
    }

    private static bool TryGetConfiguredValue(
        IReadOnlyDictionary<string, string>? mappings,
        string name,
        out string value)
    {
        if (mappings is not null)
        {
            if (mappings.TryGetValue(name, out var directValue) &&
                TryNormalize(directValue, out value))
            {
                return true;
            }

            foreach (var pair in mappings)
            {
                if (string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) &&
                    TryNormalize(pair.Value, out value))
                {
                    return true;
                }
            }
        }

        value = "";
        return false;
    }

    private static string? Normalize(string? value)
    {
        return TryNormalize(value, out var normalized) ? normalized : null;
    }

    private static bool TryNormalize(string? value, out string normalized)
    {
        normalized = value?.Trim() ?? "";
        return normalized.Length > 0;
    }
}
