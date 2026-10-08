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
        var sessionLogParser = new CodexSessionLogParser(codexOptions, presenceOptions);
        Detector = new CodexProcessDetector(codexOptions, presenceOptions, sessionLogParser);
        ModelNameProvider = new CodexModelNameProvider(codexOptions, presenceOptions, sessionLogParser);
        var accountProvider = new CodexAccountBillingTypeProvider(codexOptions.GetResolvedHomePath());
        TokenUsageProvider = new TokenUsageProvider(
            codexOptions,
            tokenUsageOptions,
            accountProvider,
            accountProvider,
            sessionLogParser);
    }

    public AppProfileKind Profile { get; }
    public CodexDetectionOptions CodexOptions { get; }
    public DiscordOptions DiscordOptions { get; }
    public CodexProcessDetector Detector { get; }
    public CodexModelNameProvider ModelNameProvider { get; }
    public TokenUsageProvider TokenUsageProvider { get; }
}
