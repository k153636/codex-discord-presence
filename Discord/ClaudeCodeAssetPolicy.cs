namespace CodexDiscordPresence;

internal static class ClaudeCodeAssetPolicy
{
    // Public Claude Code application supplied by rar-file/claude-rpc.
    internal static DiscordOptions CreateDiscordOptions() => new()
    {
        ClientId = "1506443909406920948",
        LargeImageKey = "claude_idle",
        SmallImageKey = "claude_notification",
        CompletedImageKey = null,
        ErrorImageKey = "claude_notification",
        ActivityImageKeys = Enum.GetValues<CodexActivityKind>().ToDictionary(
            kind => kind.ToString(),
            kind => kind switch
            {
                CodexActivityKind.AnalyzingProject or CodexActivityKind.Planning => "claude_thinking",
                CodexActivityKind.WaitingForInput => "claude_notification",
                CodexActivityKind.Ready or CodexActivityKind.Offline or CodexActivityKind.Stalled => "claude_idle",
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
        var name = presence.IsError ? nameof(CodexActivityKind.WaitingForInput) : presence.ActivityKind.ToString();
        return options.ActivityImageKeys.GetValueOrDefault(name) ?? options.LargeImageKey;
    }
}
