using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

// Owned by the runtime thread. Only appended transcript bytes are scanned after the initial read.
internal sealed class ClaudeCodeTokenUsageProvider(ClaudeCodeUsageStore store)
{
    private const int MaxLineBytes = 2 * 1024 * 1024;
    private readonly Dictionary<string, long> _messageTotals = new(StringComparer.Ordinal);
    private string? _sessionId;
    private string? _path;
    private long _offset;
    private long _total;
    private DateTime _lastWriteUtc;

    internal TokenUsageSnapshot GetSnapshot(ClaudeCodeSessionObservation session, TokenUsageOptions options, DateTimeOffset nowUtc)
    {
        var usage = store.GetForSession(session, nowUtc);
        var limit = usage?.RateLimit is { } observedLimit && observedLimit.ResetAtUtc > nowUtc.UtcDateTime ? observedLimit : null;
        return new(options.Enabled && !session.Ended ? ReadTotal(session) : null,
            options.Enabled ? usage?.EstimatedCostUsd : null,
            usage?.HasSubscriptionUsage == true ? "subsc" : null, limit);
    }

    private long? ReadTotal(ClaudeCodeSessionObservation session)
    {
        if (session.TranscriptPath is not { } path || !Path.IsPathFullyQualified(path) ||
            !string.Equals(Path.GetExtension(path), ".jsonl", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var info = new FileInfo(path);
            if (_sessionId != session.SessionId || _path != path || info.Length < _offset ||
                info.Length == _offset && info.LastWriteTimeUtc != _lastWriteUtc)
            {
                _sessionId = session.SessionId;
                _path = path;
                _offset = 0;
                _total = 0;
                _messageTotals.Clear();
            }
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            stream.Seek(_offset, SeekOrigin.Begin);
            var buffer = new byte[16_384];
            using var line = new MemoryStream();
            var oversized = false;
            int count;
            while ((count = stream.Read(buffer)) > 0)
            {
                for (var index = 0; index < count; index++)
                {
                    if (buffer[index] == (byte)'\n')
                    {
                        if (!oversized) ReadMessage(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length), session.SessionId);
                        line.SetLength(0);
                        oversized = false;
                        _offset = stream.Position - count + index + 1;
                    }
                    else if (line.Length < MaxLineBytes) line.WriteByte(buffer[index]);
                    else oversized = true;
                }
            }
            // An incomplete final JSONL line is reread after its newline arrives.
            _lastWriteUtc = info.LastWriteTimeUtc;
            return _messageTotals.Count == 0 ? null : _total;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            _offset = 0;
            _messageTotals.Clear();
            _total = 0;
            return null;
        }
    }

    private void ReadMessage(string line, string sessionId)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || ClaudeCodeHookParser.Text(root, "sessionId") != sessionId ||
                root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True ||
                ClaudeCodeHookParser.Text(root, "type") != "assistant" ||
                !root.TryGetProperty("message", out var message) || message.ValueKind != JsonValueKind.Object ||
                ClaudeCodeHookParser.Text(message, "id") is not { } id ||
                !message.TryGetProperty("usage", out var usage) || usage.ValueKind != JsonValueKind.Object ||
                !ReadCount(usage, "input_tokens", false, out var input) ||
                !ReadCount(usage, "output_tokens", false, out var output) ||
                !ReadCount(usage, "cache_creation_input_tokens", true, out var created) ||
                !ReadCount(usage, "cache_read_input_tokens", true, out var cached)) return;
            var total = checked(input + output + created + cached);
            // Streaming content blocks reuse message.id; keep its latest usage rather than double counting.
            var newTotal = checked(_total - _messageTotals.GetValueOrDefault(id) + total);
            _messageTotals[id] = total;
            _total = newTotal;
        }
        catch (JsonException) { /* A malformed record carries no usage evidence. */ }
    }

    private static bool ReadCount(JsonElement usage, string name, bool optional, out long count)
    {
        count = 0;
        if (!usage.TryGetProperty(name, out var value)) return optional;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out count) && count >= 0;
    }
}
