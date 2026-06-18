namespace CodexDiscordPresence;

public sealed class ProfileRuntimeState
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
        TokenUsageProvider = new TokenUsageProvider(codexOptions, tokenUsageOptions);
    }

    public AppProfileKind Profile { get; }
    public CodexDetectionOptions CodexOptions { get; }
    public DiscordOptions DiscordOptions { get; }
    public CodexProcessDetector Detector { get; }
    public CodexModelNameProvider ModelNameProvider { get; }
    public TokenUsageProvider TokenUsageProvider { get; }
    public ModelNameSnapshot? LastModelSnapshot { get; set; }
    public CodexProcessSnapshot? LastActivitySnapshot { get; set; }
    public string? LastPresenceDetails { get; set; }
    public string? LastPresenceState { get; set; }
    public string? StableCostModelName { get; set; }
    public CodexActivityKind LastActivityKind { get; set; } = CodexActivityKind.Ready;
    public int LastAnalyzingRepeatCount { get; set; } = 1;
    public DateTime? LastAnalyzingTaskStartedAt { get; set; }
    public DateTime? LastAnalyzingStartedAt { get; set; }
    public DateTime? LastActivityStartedAt { get; set; }
    public string? LastPresenceSignature { get; set; }
    public DateTime LastSuccessfulUpdateUtc { get; set; } = DateTime.MinValue;

    public void ResetPresenceCache()
    {
        LastModelSnapshot = null;
        LastActivitySnapshot = null;
        LastPresenceDetails = null;
        LastPresenceState = null;
        StableCostModelName = null;
        LastActivityKind = CodexActivityKind.Ready;
        LastAnalyzingRepeatCount = 1;
        LastAnalyzingTaskStartedAt = null;
        LastAnalyzingStartedAt = null;
        LastActivityStartedAt = null;
        LastPresenceSignature = null;
        LastSuccessfulUpdateUtc = DateTime.MinValue;
    }
}
