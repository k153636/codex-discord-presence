using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class AntigravityTranscriptActivityReader
{
    internal const long MaxTranscriptBytesToScan = 2 * 1024 * 1024;
    private const int MaxToolRecords = 256;
    private const int MaxValueLength = 128;
    private const int MaxPathLength = 2048;
    private readonly Dictionary<string, TranscriptCacheEntry> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal ProviderOperationObservation? ReadLatest(string? transcriptPath)
    {
        if (string.IsNullOrWhiteSpace(transcriptPath))
        {
            return null;
        }

        try
        {
            var file = new FileInfo(transcriptPath);
            if (!file.Exists || file.Length == 0)
            {
                return null;
            }

            var cacheKey = file.FullName;
            if (_cache.TryGetValue(cacheKey, out var cached) &&
                cached.Length == file.Length &&
                cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
            {
                return cached.Operation;
            }

            var operation = ReadLatestFromFile(file.FullName);
            _cache[cacheKey] = new TranscriptCacheEntry(
                file.Length,
                file.LastWriteTimeUtc,
                operation);
            TrimCache();
            return operation;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static ProviderOperationObservation? ReadLatestFromFile(string path)
    {
        var latestOperation = (ProviderOperationObservation?)null;
        foreach (var line in ReadTailLines(path))
        {
            var normalizedLine = line.TrimStart('\uFEFF');
            if (string.IsNullOrWhiteSpace(normalizedLine))
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(normalizedLine);
                var root = document.RootElement;
                if (!TryGetPropertyIgnoreCase(root, "tool_calls", out var toolCalls) ||
                    toolCalls.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                var recordTimestamp = ReadTimestamp(root, "created_at");
                var status = ReadText(root, "status");
                foreach (var toolCall in toolCalls.EnumerateArray().Take(MaxToolRecords))
                {
                    if (toolCall.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var toolName = ReadText(toolCall, "name");
                    var args = TryGetPropertyIgnoreCase(toolCall, "args", out var argsElement) &&
                        argsElement.ValueKind == JsonValueKind.Object
                        ? argsElement
                        : default;
                    var action = ReadText(args, "toolAction", "tool_action", "action");
                    var summary = ReadText(args, "toolSummary", "tool_summary", "summary", "command", "cmd");
                    var targetPath = ReadPath(args);
                    if (toolName is null && action is null && summary is null && targetPath is null)
                    {
                        continue;
                    }

                    latestOperation = AntigravityOperationClassifier.Classify(
                        new ProviderOperationObservation(
                            CodexOperationKind.Unknown,
                            toolName,
                            action,
                            summary,
                            targetPath,
                            IsCompleted: string.Equals(status, "DONE", StringComparison.OrdinalIgnoreCase),
                            ObservedAtUtc: recordTimestamp));
                }
            }
            catch (JsonException)
            {
                // A transcript can be observed while its last line is still being written.
            }
        }

        return latestOperation;
    }

    private static IEnumerable<string> ReadTailLines(string path)
    {
        var fileInfo = new FileInfo(path);
        var bytesToRead = (int)Math.Min(fileInfo.Length, MaxTranscriptBytesToScan);
        var bytes = new byte[bytesToRead];
        using (var stream = new FileStream(
                   path,
                   FileMode.Open,
                   FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            if (fileInfo.Length > bytesToRead)
            {
                stream.Seek(-bytesToRead, SeekOrigin.End);
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

            if (read != bytes.Length)
            {
                Array.Resize(ref bytes, read);
            }
        }

        var text = Encoding.UTF8.GetString(bytes);
        if (fileInfo.Length > bytesToRead)
        {
            var firstNewLine = text.IndexOf('\n');
            text = firstNewLine >= 0 && firstNewLine + 1 < text.Length
                ? text[(firstNewLine + 1)..]
                : string.Empty;
        }

        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'));
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, string propertyName)
    {
        var value = ReadText(root, propertyName);
        return value is not null && DateTimeOffset.TryParse(
            value,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out var timestamp)
            ? timestamp.ToUniversalTime()
            : null;
    }

    private static string? ReadPath(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var value = ReadText(
            root,
            "AbsolutePath",
            "absolute_path",
            "filePath",
            "file_path",
            "SearchPath",
            "search_path",
            "path",
            "Url",
            "url");
        return NormalizePath(value);
    }

    private static string? ReadText(JsonElement root, params string[] propertyNames)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var propertyName in propertyNames)
        {
            if (TryGetPropertyIgnoreCase(root, propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String &&
                value.GetString() is { } text)
            {
                return NormalizeText(text);
            }
        }

        return null;
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement root,
        string propertyName,
        out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        if (root.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static string? NormalizeText(string value)
    {
        var normalized = string.Join(
            ' ',
            new string(value.Trim().Where(character => !char.IsControl(character)).ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return null;
        }

        return normalized.Length <= MaxValueLength
            ? normalized
            : normalized[..MaxValueLength];
    }

    private static string? NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = new string(value.Trim().Where(character => !char.IsControl(character)).ToArray());
        return normalized.Length <= MaxPathLength
            ? normalized
            : normalized[..MaxPathLength];
    }

    private void TrimCache()
    {
        while (_cache.Count > 8)
        {
            _cache.Remove(_cache.Keys.First());
        }
    }

    private sealed record TranscriptCacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        ProviderOperationObservation? Operation);
}
