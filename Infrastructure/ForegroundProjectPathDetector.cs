using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;

namespace CodexDiscordPresence;

internal sealed class ForegroundProjectPathDetector
{
    private readonly Func<nint> _getForegroundWindow;
    private readonly Func<int, string?> _getCommandLine;
    private readonly Func<int, string?> _getProcessName;

    public ForegroundProjectPathDetector()
        : this(GetForegroundWindow, TryGetCommandLine, TryGetProcessName)
    {
    }

    internal ForegroundProjectPathDetector(
        Func<nint> getForegroundWindow,
        Func<int, string?> getCommandLine,
        Func<int, string?> getProcessName)
    {
        _getForegroundWindow = getForegroundWindow;
        _getCommandLine = getCommandLine;
        _getProcessName = getProcessName;
    }

    public string? GetFocusedProjectPath()
    {
        var foregroundWindow = _getForegroundWindow();
        if (foregroundWindow == nint.Zero)
        {
            return null;
        }

        if (!TryGetProcessId(foregroundWindow, out var processId))
        {
            return null;
        }

        var processName = _getProcessName(processId);
        if (!IsLikelyProjectHostProcess(processName))
        {
            return null;
        }

        var commandLine = _getCommandLine(processId);
        return ForegroundProjectPathParser.TryResolveProjectPathFromCommandLine(commandLine);
    }

    private static bool TryGetProcessId(nint windowHandle, out int processId)
    {
        processId = 0;
        try
        {
            _ = GetWindowThreadProcessId(windowHandle, out processId);
            return processId > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            foreach (ManagementObject process in searcher.Get())
            {
                return process["CommandLine"] as string;
            }
        }
        catch
        {
            // Foreground process inspection is best-effort.
        }

        return null;
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsLikelyProjectHostProcess(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return false;
        }

        return processName.Contains("code", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("devenv", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("rider", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("cursor", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("windsurf", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("sublime", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("explorer", StringComparison.OrdinalIgnoreCase) ||
            processName.Contains("codium", StringComparison.OrdinalIgnoreCase);
    }

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(nint hWnd, out int processId);
}

internal static class ForegroundProjectPathParser
{
    private static readonly HashSet<string> ExecutableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe",
        ".com",
        ".bat",
        ".cmd",
        ".ps1"
    };

    internal static string? TryResolveProjectPathFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
        {
            return null;
        }

        if (!TrySplitCommandLine(commandLine, out var arguments))
        {
            return null;
        }

        foreach (var argument in arguments.Skip(1))
        {
            if (TryResolveProjectPathFromArgument(argument, out var projectPath))
            {
                return projectPath;
            }
        }

        return arguments.Length == 1 && TryResolveProjectPathFromArgument(arguments[0], out var singleArgumentProjectPath)
            ? singleArgumentProjectPath
            : null;
    }

    internal static bool TryResolveProjectPathFromArgument(string argument, out string? projectPath)
    {
        projectPath = null;

        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var normalizedArgument = argument.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(normalizedArgument))
        {
            return false;
        }

        if (TryResolveFileUri(normalizedArgument, out var uriPath))
        {
            normalizedArgument = uriPath;
        }

        if (Directory.Exists(normalizedArgument))
        {
            projectPath = Path.GetFullPath(normalizedArgument);
            return true;
        }

        if (File.Exists(normalizedArgument))
        {
            var extension = Path.GetExtension(normalizedArgument);
            if (!ExecutableExtensions.Contains(extension))
            {
                projectPath = Path.GetFullPath(Path.GetDirectoryName(normalizedArgument) ?? normalizedArgument);
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveFileUri(string value, out string path)
    {
        path = "";

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile)
        {
            return false;
        }

        path = Uri.UnescapeDataString(uri.LocalPath);
        return true;
    }

    private static bool TrySplitCommandLine(string commandLine, out string[] arguments)
    {
        arguments = [];
        try
        {
            var argv = CommandLineToArgvW(commandLine, out var argc);
            if (argv == nint.Zero || argc <= 0)
            {
                return false;
            }

            try
            {
                arguments = new string[argc];
                for (var i = 0; i < argc; i++)
                {
                    var ptr = Marshal.ReadIntPtr(argv, i * IntPtr.Size);
                    arguments[i] = Marshal.PtrToStringUni(ptr) ?? "";
                }

                return true;
            }
            finally
            {
                _ = LocalFree(argv);
            }
        }
        catch
        {
            return false;
        }
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern nint CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern nint LocalFree(nint hMem);
}
