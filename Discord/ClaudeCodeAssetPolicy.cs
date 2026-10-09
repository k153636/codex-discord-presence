namespace CodexDiscordPresence;

internal static class ClaudeCodeAssetPolicy
{
    internal const string NotificationImageKey = "claude_notification";
    // 300 frames at 20 ms per frame in clawd-notification.gif.
    internal static readonly TimeSpan NotificationLoopDuration = TimeSpan.FromSeconds(6);
    // Public Claude Code application supplied by rar-file/claude-rpc.
    internal static DiscordOptions CreateDiscordOptions() => new()
    {
        ClientId = "1506443909406920948",
        LargeImageKey = "claude_idle",
        SmallImageKey = null,
        CompletedImageKey = null,
        ErrorImageKey = "claude_idle",
        ActivityImageKeys = Enum.GetValues<CodexActivityKind>().ToDictionary(
            kind => kind.ToString(),
            kind => kind switch
            {
                CodexActivityKind.AnalyzingProject or CodexActivityKind.Planning => "claude_thinking",
                CodexActivityKind.WaitingForInput or CodexActivityKind.Ready or
                    CodexActivityKind.Offline or CodexActivityKind.Stalled => "claude_idle",
                _ => "claude_working"
            }),
        RunningCommandImageKeys = new(),
        ExternalImageUrls = new()
        {
            ["claude_idle"] = "https://cdn.qualit.ly/clawd-sleeping.gif",
            ["claude_thinking"] = "https://cdn.qualit.ly/clawd-working-typing.gif",
            ["claude_working"] = "https://cdn.qualit.ly/clawd-working-building.gif",
            ["claude_notification"] = "https://cdn.qualit.ly/clawd-notification.gif"
        }
    };

    internal static string? ResolveImageKey(DiscordOptions options, RenderedPresence presence)
    {
        if (presence.LargeImageKeyOverride == NotificationImageKey && presence.ActivityKind.IsActive() && !presence.IsError)
        {
            return NotificationImageKey;
        }
        var kind = presence.IsError ? CodexActivityKind.WaitingForInput : presence.ActivityKind;
        return DiscordAssetKeyResolver.ResolveActivityImageKey(options, kind) ?? options.LargeImageKey;
    }

    internal static string? ResolveSubagentImageKey(DiscordOptions options, CodexActivityKind activityKind) =>
        DiscordAssetKeyResolver.ResolveActivityImageKey(options, activityKind);
}
