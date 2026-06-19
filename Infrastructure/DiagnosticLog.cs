using System.Text;

namespace CodexDiscordPresence;

public sealed class DiagnosticLog : IDisposable
{
    private readonly StreamWriter _writer;
    private readonly object _gate = new();

    public DiagnosticLog(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path) ?? ".");
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite), Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public string Path { get; }

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
        _writer.Dispose();
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

            _writer.WriteLine(line);
        }
    }
}
