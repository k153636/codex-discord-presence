namespace CodexDiscordPresence;

internal readonly record struct DiscordSmallImageSelection(
    string? ImageReference,
    string? Text);

internal static class DiscordSubagentSmallImagePolicy
{
    public static DiscordSmallImageSelection Resolve(
        DiscordOptions options,
        RenderedPresence presence)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(presence);

        var activity = presence.SubagentActivity;
        if (activity is null)
        {
            return default;
        }

        var workKind = activity.TryGetHomogeneousWorkKind(out var homogeneousWorkKind)
            ? homogeneousWorkKind
            : SubagentWorkKind.Coordinating;
        var imageKey = ResolveProviderImageKey(options, presence.ProviderId, workKind);
        var imageReference = DiscordAssetKeyResolver.ResolveImageReference(options, imageKey);
        return imageReference is null
            ? default
            : new DiscordSmallImageSelection(imageReference, activity.FormatSmallImageText());
    }

    private static string? ResolveProviderImageKey(
        DiscordOptions options,
        string? providerId,
        SubagentWorkKind workKind)
    {
        if (providerId == ProviderIds.Codex)
        {
            var activityKind = ToActivityKind(workKind);
            return DiscordAssetKeyResolver.ResolveActivityImageKey(options, activityKind);
        }

        if (providerId == ProviderIds.ClaudeCode)
        {
            return ClaudeCodeAssetPolicy.ResolveSubagentImageKey(options, ToActivityKind(workKind));
        }

        if (providerId == ProviderIds.Antigravity)
        {
            return DiscordAssetKeyResolver.ResolveProviderLargeImageKey(providerId);
        }

        return null;
    }

    private static CodexActivityKind ToActivityKind(SubagentWorkKind workKind) => workKind switch
    {
        SubagentWorkKind.Thinking => CodexActivityKind.AnalyzingProject,
        SubagentWorkKind.Editing => CodexActivityKind.ApplyingEdits,
        SubagentWorkKind.Reading => CodexActivityKind.ReadingFiles,
        SubagentWorkKind.Researching => CodexActivityKind.Researching,
        SubagentWorkKind.RunningCommand => CodexActivityKind.RunningCommand,
        _ => CodexActivityKind.CoordinatingChanges
    };
}
