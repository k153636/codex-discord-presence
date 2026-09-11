namespace CodexDiscordPresence;

public class PresenceDispatchCache
{
    public string? LastPresenceDetails { get; set; }
    public string? LastPresenceState { get; set; }
    public string? LastPresenceLargeImageKey { get; set; }
    public string? LastPresenceSignature { get; set; }
    public DateTime LastSuccessfulUpdateUtc { get; set; } = DateTime.MinValue;

    public virtual void ResetPresenceCache()
    {
        LastPresenceDetails = null;
        LastPresenceState = null;
        LastPresenceLargeImageKey = null;
        LastPresenceSignature = null;
        LastSuccessfulUpdateUtc = DateTime.MinValue;
    }
}

public class PresenceRuntimeCache : PresenceDispatchCache
{
    public ModelNameSnapshot? LastModelSnapshot { get; set; }
    public IPresenceActivitySnapshot? LastActivitySnapshot { get; set; }
    public string? StableCostModelName { get; set; }
    public CodexActivityKind LastActivityKind { get; set; } = CodexActivityKind.Ready;
    public int LastAnalyzingRepeatCount { get; set; } = 1;
    public DateTime? LastAnalyzingTaskStartedAt { get; set; }
    public DateTime? LastAnalyzingStartedAt { get; set; }
    public DateTime? LastActivityStartedAt { get; set; }

    public override void ResetPresenceCache()
    {
        base.ResetPresenceCache();
        LastModelSnapshot = null;
        LastActivitySnapshot = null;
        StableCostModelName = null;
        LastActivityKind = CodexActivityKind.Ready;
        LastAnalyzingRepeatCount = 1;
        LastAnalyzingTaskStartedAt = null;
        LastAnalyzingStartedAt = null;
        LastActivityStartedAt = null;
    }
}
