namespace CodexDiscordPresence;

internal static class ReleaseVersionHistory
{
    // Match archived release identities as well as tags. Future 1.x releases retain
    // their normal SemVer meaning; display-only renumbering must not cause a downgrade.
    public static SemanticVersion Resolve(long releaseId, string tag, SemanticVersion parsedVersion) =>
        (releaseId, tag) switch
        {
            (341494232, "v1.0.0") => new(0, 1, 0, null),
            (341678513, "v1.1.0") => new(0, 1, 1, null),
            (383922312, "v1.2.0") => new(0, 2, 0, null),
            (385872123, "v1.2.5") => new(0, 2, 5, null),
            _ => parsedVersion
        };
}
