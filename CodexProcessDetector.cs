using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class CodexProcessDetector
{
    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;

    public CodexProcessDetector(CodexDetectionOptions options, PresenceTemplateOptions presenceOptions)
    {
        _options = options;
        _presenceOptions = presenceOptions;
    }

    public CodexProcessSnapshot GetSnapshot()
    {
        foreach (var process in Process.GetProcesses())
        {
            try
            {
                if (Matches(process.ProcessName, _options.ProcessNameContains) ||
                    Matches(process.MainWindowTitle, _options.WindowTitleContains))
                {
                    var isThinking = DetermineIfThinking();
                    return new CodexProcessSnapshot(true, process.ProcessName, isThinking);
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

        return new CodexProcessSnapshot(false, null, false);
    }

    internal bool DetermineIfThinking()
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return false;
        }

        try
        {
            var latestFile = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .FirstOrDefault();

            if (latestFile == null)
            {
                return false;
            }

            return AnalyzeSessionFileForThinking(latestFile.FullName);
        }
        catch
        {
            return false;
        }
    }

    private bool AnalyzeSessionFileForThinking(string path)
    {
        bool isThinking = false;
        DateTime? lastTaskStartedTime = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Contains("\"task_started\"", StringComparison.Ordinal))
                {
                    isThinking = true;
                    lastTaskStartedTime = ExtractTimestamp(line);
                }
                else if (line.Contains("\"task_complete\"", StringComparison.Ordinal))
                {
                    isThinking = false;
                }
            }
        }
        catch
        {
            // Return current state found up to exception, or false
        }

        if (isThinking && lastTaskStartedTime.HasValue)
        {
            var elapsed = DateTime.UtcNow - lastTaskStartedTime.Value;
            if (elapsed.TotalMinutes >= _presenceOptions.ThinkingStaleTimeoutMinutes)
            {
                isThinking = false;
            }
        }

        return isThinking;
    }

    private static DateTime? ExtractTimestamp(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("timestamp", out var prop))
            {
                var timeStr = prop.GetString();
                if (DateTime.TryParse(timeStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
                {
                    return dt;
                }
            }
        }
        catch
        {
            // Ignore parse errors
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
