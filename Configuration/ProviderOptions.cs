namespace CodexDiscordPresence;

public static class ProviderIds
{
    public const string Codex = "codex";
    public const string Antigravity = "antigravity";
    public const string ClaudeCode = "claude-code";
}

public sealed class ProviderOptions
{
    public bool Enabled { get; set; }
}
