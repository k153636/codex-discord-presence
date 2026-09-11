namespace CodexDiscordPresence;

internal sealed class ProviderRuntimeState : PresenceRuntimeCache
{
    internal ProviderRuntimeState(string providerId, DiscordOptions discordOptions)
    {
        if (string.IsNullOrWhiteSpace(providerId))
        {
            throw new ArgumentException("A provider ID is required.", nameof(providerId));
        }

        ProviderId = providerId.Trim();
        DiscordOptions = discordOptions ?? throw new ArgumentNullException(nameof(discordOptions));
    }

    internal string ProviderId { get; }
    internal DiscordOptions DiscordOptions { get; }
}
