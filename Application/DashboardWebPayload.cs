using System.Collections.Concurrent;
using System.Drawing.Imaging;

namespace CodexDiscordPresence;

internal sealed record DashboardWebPayload(
    string? Provider,
    IReadOnlyDictionary<string, bool> Providers,
    string OwnerName,
    string Project,
    string Connection,
    string Details,
    string Activity,
    string ActivityType,
    string Elapsed,
    string? LargeImage,
    string? SmallImage,
    string? SmallText,
    string? ButtonLabel,
    DashboardWebMetric[] Metrics,
    string UsageNote,
    bool HasPublishedPresence,
    bool Enabled)
{
    private static readonly ConcurrentDictionary<string, string> StaticFrames = new(StringComparer.OrdinalIgnoreCase);
    internal static DashboardWebPayload Create(PresenceDashboardSnapshot snapshot, PresenceRuntimeState runtime, DateTime nowUtc)
    {
        var owner = runtime.Enabled && !snapshot.HasNoActiveProvider && snapshot.Presence is not null
            ? snapshot.ProviderId : null;
        var presence = snapshot.PublishedPresence;
        var empty = DashboardTextFormatter.FormatEmptyPreview(snapshot, runtime.Enabled);
        var metrics = DashboardTextFormatter.CreateMetrics(snapshot with
        {
            HasNoActiveProvider = owner is null
        }, nowUtc).Select(metric => new DashboardWebMetric(metric.Label, metric.Value, metric.ProgressPercent)).ToArray();
        return new(owner, new[] {ProviderIds.Codex, ProviderIds.ClaudeCode, ProviderIds.Antigravity}.ToDictionary(id => id,
                id => runtime.IsProviderEnabled(id, id == ProviderIds.Codex)),
            DashboardTextFormatter.FormatProviderName(owner), snapshot.ProjectName ?? "",
            DashboardTextFormatter.FormatConnection(snapshot).ToLowerInvariant(),
            presence?.Details ?? empty.Title, presence?.State ?? empty.Description,
            DashboardTextFormatter.FormatActivityType(presence).TrimEnd(':'),
            DashboardTextFormatter.FormatElapsed(presence?.StartedAtUtc, nowUtc),
            LocalImageReference(presence?.LargeImageKey), LocalImageReference(presence?.SmallImageKey),
            string.IsNullOrWhiteSpace(presence?.SmallImageKey) ? null : presence.SmallImageText,
            presence?.Buttons.FirstOrDefault()?.Label, metrics,
            DashboardTextFormatter.FormatUsageNote(snapshot with {HasNoActiveProvider = owner is null}),
            presence is not null, runtime.Enabled);
    }

    internal static string? LocalImageReference(string? reference, bool? animationsEnabled = null)
    {
        if (string.IsNullOrWhiteSpace(reference)) return null;
        var fileName = Uri.TryCreate(reference, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            ? Path.GetFileName(uri.LocalPath) : reference;
        fileName = fileName switch
        {
            "claude_idle" => "clawd-sleeping.gif",
            "claude_thinking" => "clawd-working-typing.gif",
            "claude_working" => "clawd-working-building.gif",
            "claude_notification" => "clawd-notification.gif",
            _ => fileName
        };
        // Keep the original GIF; WebView2 uses the local file before any remote fallback.
        if (fileName.IndexOfAny(['/', '\\', ':']) < 0)
        {
            var candidates = fileName.StartsWith("clawd-", StringComparison.OrdinalIgnoreCase)
                ? new[] {"ClaudeCode/" + fileName, "ClaudeCode/" + fileName + ".gif", "ClaudeCode/" + fileName + ".png"}
                : new[] {fileName, fileName + ".gif", fileName + ".png"};
            foreach (var candidate in candidates)
            {
                var path = Path.Combine(AppContext.BaseDirectory, "Assets", "RpcArt", candidate);
                if (File.Exists(path))
                {
                    if (!(animationsEnabled ?? DashboardAnimationPreferences.AnimationsEnabled) && Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase))
                        return StaticFrames.GetOrAdd(path, ReadStaticFrame);
                    return "https://rpc-art.local/" + string.Join("/", candidate.Split('/').Select(Uri.EscapeDataString));
                }
            }
        }
        return uri?.Scheme is "http" or "https" ? uri.AbsoluteUri : null;
    }

    private static string ReadStaticFrame(string path)
    {
        using var image = System.Drawing.Image.FromFile(path);
        using var stream = new MemoryStream();
        image.Save(stream, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(stream.ToArray());
    }
}

internal sealed record DashboardWebMetric(string Label, string Value, double? ProgressPercent);
