namespace CodexDiscordPresence;

public sealed class ProfileRuntimeState : PresenceRuntimeCache
{
    public ProfileRuntimeState(
        AppProfileKind profile,
        CodexDetectionOptions codexOptions,
        DiscordOptions discordOptions,
        PresenceTemplateOptions presenceOptions,
        TokenUsageOptions tokenUsageOptions)
    {
        Profile = profile;
        CodexOptions = codexOptions;
        DiscordOptions = discordOptions;
        Detector = new CodexProcessDetector(codexOptions, presenceOptions);
        ModelNameProvider = new CodexModelNameProvider(codexOptions, presenceOptions);
        var accountProvider = new CodexAccountBillingTypeProvider(codexOptions.GetResolvedHomePath());
        TokenUsageProvider = new TokenUsageProvider(
            codexOptions,
            tokenUsageOptions,
            accountProvider,
            accountProvider);
    }

    public AppProfileKind Profile { get; }
    public CodexDetectionOptions CodexOptions { get; }
    public DiscordOptions DiscordOptions { get; }
    public CodexProcessDetector Detector { get; }
    public CodexModelNameProvider ModelNameProvider { get; }
    public TokenUsageProvider TokenUsageProvider { get; }
}
