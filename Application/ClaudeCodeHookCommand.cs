using System.Text;

namespace CodexDiscordPresence;

internal static class ClaudeCodeHookCommand
{
    internal static async Task<int> RunAsync()
    {
        try
        {
            using var input = Console.OpenStandardInput();
            using var buffer = new MemoryStream();
            var bytes = new byte[8192];
            int count;
            while ((count = await input.ReadAsync(bytes)) > 0)
            {
                if (buffer.Length + count > ClaudeCodeHookParser.MaxPayloadBytes)
                {
                    return 0;
                }
                buffer.Write(bytes, 0, count);
            }
            var hookEvent = ClaudeCodeHookParser.Parse(Encoding.UTF8.GetString(buffer.ToArray()), DateTimeOffset.UtcNow);
            if (hookEvent is not null)
            {
                var paths = AppPaths.Create(AppProfileKind.Codex);
                new ClaudeCodeObservationStore(Path.Combine(paths.AppDataDirectory, "claude-code", "sessions")).Write(hookEvent);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Hooks are observational only: a local persistence failure must never block Claude.
            var paths = AppPaths.Create(AppProfileKind.Codex);
            using var log = DiagnosticLog.Create(paths.LogsDirectory);
            log.Warn($"Claude Code hook observation could not be saved ({ex.GetType().Name}).");
        }
        return 0;
    }
}
