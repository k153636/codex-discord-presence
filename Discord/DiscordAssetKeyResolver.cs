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

        if (presence.IsError)
        {
            if (TryNormalize(options.ErrorImageKey, out var errorImageKey))
            {
                return errorImageKey;
            }
        }

        if (presence.IsSuccessfulCompletion &&
            presence.ActivityKind is (CodexActivityKind.Ready or CodexActivityKind.WaitingForInput) &&
            IsWithinCompletedImageHold(presence.WaitingStartedAt, options.CompletedImageHoldSeconds) &&
            TryNormalize(options.CompletedImageKey, out var completedImageKey))
        {
            return completedImageKey;
        }

        if (presence.ActivityKind == CodexActivityKind.RunningCommand &&
            TryGetConfiguredKey(
                options.RunningCommandImageKeys,
                presence.RunningCommandKind.ToString(),
                out var commandImageKey))
        {
            return SanitizeNonErrorImageKey(options, presence, commandImageKey);
        }

        if (!presence.IsThinking &&
            presence.ActivityKind.IsThinking())
        {
            return TryGetConfiguredKey(
                    options.ActivityImageKeys,
                    nameof(CodexActivityKind.Ready),
                    out var waitingImageKey)
                ? waitingImageKey
                : "rpc_sleeping";
        }

        if (presence.ActivityKind == CodexActivityKind.Stalled)
        {
            return ResolveWaitingImageKey(options);
        }

        return TryGetConfiguredKey(
                options.ActivityImageKeys,
                presence.ActivityKind.ToString(),
                out var activityImageKey)
            ? SanitizeNonErrorImageKey(options, presence, activityImageKey)
            : ResolveFallbackImageKey(options, presence);
    }

    private static string? ResolveFallbackImageKey(DiscordOptions options, RenderedPresence presence)
    {
        var fallbackImageKey = Normalize(options.LargeImageKey);
        return fallbackImageKey is null
            ? null
            : SanitizeNonErrorImageKey(options, presence, fallbackImageKey);
    }

    private static string SanitizeNonErrorImageKey(
        DiscordOptions options,
        RenderedPresence presence,
        string imageKey)
    {
        if (presence.IsError ||
            !IsErrorImageKey(options, imageKey))
        {
            return imageKey;
        }

        return ResolveWaitingImageKey(options);
    }

    private static string ResolveWaitingImageKey(DiscordOptions options)
    {
        return TryGetConfiguredKey(
                options.ActivityImageKeys,
                nameof(CodexActivityKind.Ready),
                out var waitingImageKey) &&
            !IsErrorImageKey(options, waitingImageKey)
            ? waitingImageKey
            : "rpc_sleeping";
    }

    private static bool IsErrorImageKey(DiscordOptions options, string imageKey)
    {
        return (TryNormalize(options.ErrorImageKey, out var errorImageKey) &&
                string.Equals(imageKey, errorImageKey, StringComparison.OrdinalIgnoreCase)) ||
            string.Equals(imageKey, "rpc_error", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWithinCompletedImageHold(DateTime? waitingStartedAt, int holdSeconds)
    {
        if (!waitingStartedAt.HasValue)
        {
            return false;
        }

        var elapsed = DateTime.UtcNow - waitingStartedAt.Value;
        return elapsed >= TimeSpan.Zero && elapsed < TimeSpan.FromSeconds(Math.Max(0, holdSeconds));
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
