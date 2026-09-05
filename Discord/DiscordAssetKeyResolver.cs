namespace CodexDiscordPresence;

internal static class DiscordAssetKeyResolver
{
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
