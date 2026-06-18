using System.Diagnostics;
using System.Management;

namespace CodexDiscordPresence;

internal sealed record CodexProcessMatch(
    string ProcessName,
    CodexProcessDetectionKind DetectionKind,
    string? ExecutablePath,
    string? CommandLine);

internal sealed class CodexProcessNameMatcher
{
    private readonly CodexDetectionOptions _options;

    public CodexProcessNameMatcher(CodexDetectionOptions options)
    {
        _options = options;
    }

    public CodexProcessMatch? FindMatchingProcess()
    {
        if (_options.CommandLineContains.Length > 0)
        {
            var nodeProcessMatch = FindMatchingNodeProcess();
            if (nodeProcessMatch is not null)
            {
                return nodeProcessMatch;
            }
        }

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Matches(process.ProcessName, _options.ProcessNameContains))
                {
                    return new CodexProcessMatch(process.ProcessName, CodexProcessDetectionKind.ProcessName, null, null);
                }

                if (Matches(process.MainWindowTitle, _options.WindowTitleContains))
                {
                    return new CodexProcessMatch(process.ProcessName, CodexProcessDetectionKind.WindowTitle, null, null);
                }

                if (_options.ExecutablePathContains.Length > 0 && Matches(GetExecutablePath(process), _options.ExecutablePathContains))
                {
                    return new CodexProcessMatch(process.ProcessName, CodexProcessDetectionKind.ExecutablePath, GetExecutablePath(process), null);
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

    private CodexProcessMatch? FindMatchingNodeProcess()
    {
        foreach (var process in Process.GetProcessesByName("node"))
        {
            try
            {
                if (TryGetCommandLine(process.Id, out var commandLine) &&
                    Matches(commandLine, _options.CommandLineContains))
                {
                    return new CodexProcessMatch(process.ProcessName, CodexProcessDetectionKind.CommandLine, null, commandLine);
                }
            }
            catch
            {
                // Ignore node processes we cannot inspect.
            }
            finally
            {
                process.Dispose();
            }
        }

        return null;
    }

    private static string? GetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    private static bool TryGetCommandLine(int processId, out string? commandLine)
    {
        commandLine = null;

        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject process in searcher.Get())
            {
                commandLine = process["CommandLine"] as string;
                return true;
            }
        }
        catch
        {
            // WMI can be unavailable or deny access. Fall back to basic matching.
        }

        return false;
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
