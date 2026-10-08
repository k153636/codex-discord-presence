using System.Text;

namespace CodexDiscordPresence;

internal static class ClaudeCodeTranscriptTail
{
    private const int MaxBytes = 2 * 1024 * 1024;

    internal static IEnumerable<string> ReadLines(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var bytes = new byte[(int)Math.Min(stream.Length, MaxBytes)];
        var hasPartialFirstLine = stream.Length > bytes.Length;
        if (hasPartialFirstLine)
        {
            stream.Seek(-bytes.Length, SeekOrigin.End);
        }
        var read = 0;
        while (read < bytes.Length)
        {
            var count = stream.Read(bytes, read, bytes.Length - read);
            if (count == 0)
            {
                break;
            }
            read += count;
        }
        var text = Encoding.UTF8.GetString(bytes, 0, read);
        using var reader = new StringReader(text);
        if (hasPartialFirstLine)
        {
            reader.ReadLine();
        }
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }
}
