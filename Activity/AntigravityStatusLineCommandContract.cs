namespace CodexDiscordPresence;

internal static class AntigravityStatusLineCommandContract
{
    internal const string SettingsPath = "~/.gemini/antigravity-cli/settings.json";
    internal const string SettingsPropertyName = "statusLine";
    internal const string CommandType = "command";

    internal const string StandardInput = "stdin: detailed Antigravity agent-state JSON payload";
    internal const string StandardOutput = "stdout: one formatted status-line string";
}
