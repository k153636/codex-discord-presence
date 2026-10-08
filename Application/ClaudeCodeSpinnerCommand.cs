using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace CodexDiscordPresence;

// Console attachment is process-wide; run only in the short-lived helper, never in the tray process.
internal static class ClaudeCodeSpinnerCommand
{
    internal static int Run()
    {
        using var output = Console.OpenStandardOutput();
        var processes = Process.GetProcessesByName("claude");
        try
        {
            var label = processes.Length == 1 ? ReadLabel((uint)processes[0].Id) : null;
            using var writer = new StreamWriter(output, new UTF8Encoding(false));
            writer.Write(label ?? "");
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
        return 0;
    }

    private static string? ReadLabel(uint processId)
    {
        FreeConsole();
        if (!AttachConsole(processId))
        {
            return null;
        }
        try
        {
            var title = new StringBuilder(4096);
            GetConsoleTitle(title, (uint)title.Capacity);
            return ReadTerminalLabel(title.ToString());
        }
        finally
        {
            FreeConsole();
        }
    }

    private static string? ReadTerminalLabel(string consoleTitle)
    {
        if (string.IsNullOrWhiteSpace(consoleTitle))
        {
            return null;
        }
        var terminals = Process.GetProcessesByName("WindowsTerminal");
        try
        {
            var windows = new List<AutomationElement>();
            foreach (var terminal in terminals)
            {
                var condition = new AndCondition(
                    new PropertyCondition(AutomationElement.ProcessIdProperty, terminal.Id),
                    new PropertyCondition(AutomationElement.NameProperty, consoleTitle));
                windows.AddRange(AutomationElement.RootElement.FindAll(TreeScope.Children, condition)
                    .Cast<AutomationElement>());
            }
            if (windows.Count != 1)
            {
                return null;
            }
            var labels = new List<string>();
            foreach (AutomationElement element in windows[0].FindAll(TreeScope.Descendants,
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text)))
            {
                if (element.TryGetCurrentPattern(TextPattern.Pattern, out var pattern))
                {
                    var visibleText = string.Join("\n", ((TextPattern)pattern).GetVisibleRanges()
                        .Select(range => range.GetText(131_072)));
                    if (ClaudeCodeSpinnerLabel.ParseScreen(visibleText) is { } label)
                    {
                        labels.Add(label);
                    }
                }
            }
            return labels.Count == 1 ? labels[0] : null;
        }
        catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return null;
        }
        finally
        {
            foreach (var terminal in terminals)
            {
                terminal.Dispose();
            }
        }
    }

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint processId);
    [DllImport("kernel32.dll")]
    private static extern bool FreeConsole();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern uint GetConsoleTitle(StringBuilder output, uint length);
}
