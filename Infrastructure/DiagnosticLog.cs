using System.Text;

namespace CodexDiscordPresence;

public sealed class DiagnosticLog : IDisposable
{
    private readonly string _logsDirectory;
    private readonly string _baseFileName;
    private readonly long _maxFileSizeBytes;
    private readonly object _gate = new();
    private int _rotationIndex;

    public DiagnosticLog(string path, long maxFileSizeBytes = 1_048_576)
    {
        var directory = System.IO.Path.GetDirectoryName(path) ?? ".";
        _logsDirectory = directory;
        _baseFileName = System.IO.Path.GetFileNameWithoutExtension(path);
        _maxFileSizeBytes = Math.Max(1, maxFileSizeBytes);
        Directory.CreateDirectory(directory);
        Path = path;
        EnsureFileExists(Path);
    }

    public string Path { get; private set; }

    public static DiagnosticLog Create(string logsDirectory)
    {
        Directory.CreateDirectory(logsDirectory);
        var fileName = $"presence-{DateTime.UtcNow:yyyyMMdd-HHmmssZ}-{Environment.ProcessId}.log";
        return new DiagnosticLog(System.IO.Path.Combine(logsDirectory, fileName));
    }

    public void Info(string message) => Write("INFO", message, false);

    public void Warn(string message) => Write("WARN", message, false);

    public void Error(string message) => Write("ERROR", message, true);

    public void Error(string message, Exception exception) => Write("ERROR", $"{message} :: {exception}", true);

    public void Dispose()
    {
    }

    private void Write(string level, string message, bool error)
    {
        var normalizedMessage = message.ReplaceLineEndings(" | ");
        var line = $"{DateTime.UtcNow:O} [{level}] {normalizedMessage}";
        lock (_gate)
        {
            if (error)
            {
                Console.Error.WriteLine(line);
            }
            else
            {
                Console.WriteLine(line);
            }

            AppendLine(Path, line);

            var currentLength = GetFileLength(Path);
            if (currentLength >= _maxFileSizeBytes)
            {
                RotateToNextFile();
            }
        }
    }

    private void RotateToNextFile()
    {
        _rotationIndex++;
        Path = BuildRotatedPath();
        EnsureFileExists(Path);
    }

    private void AppendLine(string path, string line)
    {
        File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
    }

    private static void EnsureFileExists(string path)
    {
        if (!File.Exists(path))
        {
            File.WriteAllText(path, string.Empty, Encoding.UTF8);
        }
    }

    private static long GetFileLength(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch
        {
            return 0;
        }
    }

    private string BuildRotatedPath()
    {
        var suffix = _rotationIndex == 0 ? "" : $"-{_rotationIndex}";
        return System.IO.Path.Combine(_logsDirectory, $"{_baseFileName}{suffix}.log");
    }
}
