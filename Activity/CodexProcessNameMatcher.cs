using System.Diagnostics;

namespace CodexDiscordPresence;

internal sealed class CodexProcessNameMatcher
{
    private readonly CodexDetectionOptions _options;

    public CodexProcessNameMatcher(CodexDetectionOptions options)
    {
        _options = options;
    }

    public string? FindMatchingProcessName()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Matches(process.ProcessName, _options.ProcessNameContains) ||
                    Matches(process.MainWindowTitle, _options.WindowTitleContains))
                {
                    return process.ProcessName;
                }
            }
            catch
            {
                // Some system processes deny metadata access. They are irrelevant for Codex detection.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static bool Matches(string? value, IEnumerable<string> needles)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return needles.Any(needle =>
            !string.IsNullOrWhiteSpace(needle) &&
            value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }
}
