using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

internal sealed record AntigravityPendingInputObservation(
    bool IsPending,
    string? ToolName,
    int? StepIndex,
    DateTimeOffset? RequestedAtUtc);

internal sealed class AntigravityCliConfirmationReader
{
    private const long MaxLogBytesToScan = 1024 * 1024;
    private static readonly Regex LogTimestampRegex = new(
        "^I(?<month>\\d{2})(?<day>\\d{2})\\s+(?<time>\\d{2}:\\d{2}:\\d{2}\\.\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex SurfaceRegex = new(
        "Surfacing tool confirmation:\\s+\\\"(?<tool>[^\\\"]+)\\\" at step (?<step>\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex ResponseRegex = new(
        "Responding to tool confirmation:\\s+convID=(?<conversation>[^,\\s]+),\\s+stepIdx=(?<step>\\d+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly Regex CancellationRegex = new(
        "Cancelling (?:in-progress response|conversation) for conversation\\s+(?<conversation>[0-9a-zA-Z-]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly string _logDirectoryPath;
    private readonly TimeZoneInfo _logTimeZone;
    private readonly Dictionary<string, CachedLogSnapshot> _cache =
        new(StringComparer.OrdinalIgnoreCase);

    internal AntigravityCliConfirmationReader()
        : this(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".gemini",
                "antigravity-cli",
                "log"),
            TimeZoneInfo.Local)
    {
    }

    internal AntigravityCliConfirmationReader(
        string logDirectoryPath,
        TimeZoneInfo logTimeZone)
    {
        _logDirectoryPath = Path.GetFullPath(logDirectoryPath);
        _logTimeZone = logTimeZone;
    }

    internal AntigravityPendingInputObservation ReadPending(
        string? conversationId,
        DateTimeOffset observationAtUtc,
        DateTimeOffset nowUtc)
    {
        var logPath = FindLatestLogPath();
        if (logPath is null)
        {
            return new(false, null, null, null);
        }

        try
        {
            var snapshot = ReadLogSnapshot(logPath, nowUtc);
            var latestRequest = snapshot.LatestRequest;

            if (latestRequest is null ||
                latestRequest.RequestedAtUtc > observationAtUtc.AddSeconds(10) ||
                nowUtc - latestRequest.RequestedAtUtc > TimeSpan.FromMinutes(10) ||
                nowUtc < latestRequest.RequestedAtUtc)
            {
                return new(false, null, null, null);
            }

            if (snapshot.Responses.TryGetValue(latestRequest.StepIndex, out var resolution) &&
                resolution.ResolvedAtUtc >= latestRequest.RequestedAtUtc &&
                (string.IsNullOrWhiteSpace(conversationId) ||
                 string.Equals(resolution.ConversationId, conversationId, StringComparison.Ordinal)))
            {
                return new(false, null, null, null);
            }

            if (!string.IsNullOrWhiteSpace(conversationId) &&
                snapshot.Cancellations.TryGetValue(conversationId, out var cancelledAt) &&
                cancelledAt >= latestRequest.RequestedAtUtc)
            {
                return new(false, null, null, null);
            }

            return new(
                true,
                latestRequest.ToolName,
                latestRequest.StepIndex,
                latestRequest.RequestedAtUtc);
        }
        catch (IOException)
        {
            return new(false, null, null, null);
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, null, null, null);
        }
    }

    private ConfirmationLogSnapshot ReadLogSnapshot(string path, DateTimeOffset nowUtc)
    {
        var fileInfo = new FileInfo(path);
        if (_cache.TryGetValue(path, out var cached) &&
            cached.Length == fileInfo.Length &&
            cached.LastWriteTimeUtc == fileInfo.LastWriteTimeUtc)
        {
            return cached.Snapshot;
        }

        var latestRequest = (ConfirmationRequest?)null;
        var responses = new Dictionary<int, ConfirmationResolution>();
        var cancellations = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var line in ReadTailLines(path))
        {
            var normalizedLine = line.TrimStart('\uFEFF');
            if (!TryReadLogTimestamp(normalizedLine, nowUtc, out var timestampUtc))
            {
                continue;
            }

            var surface = SurfaceRegex.Match(normalizedLine);
            if (surface.Success &&
                int.TryParse(surface.Groups["step"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var stepIndex))
            {
                latestRequest = new ConfirmationRequest(
                    timestampUtc,
                    stepIndex,
                    surface.Groups["tool"].Value.Trim());
                continue;
            }

            var response = ResponseRegex.Match(normalizedLine);
            if (response.Success &&
                int.TryParse(response.Groups["step"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out stepIndex))
            {
                responses[stepIndex] = new ConfirmationResolution(
                    response.Groups["conversation"].Value,
                    timestampUtc);
                continue;
            }

            var cancellation = CancellationRegex.Match(normalizedLine);
            if (cancellation.Success)
            {
                cancellations[cancellation.Groups["conversation"].Value] = timestampUtc;
            }
        }

        var snapshot = new ConfirmationLogSnapshot(latestRequest, responses, cancellations);
        _cache[path] = new CachedLogSnapshot(fileInfo.Length, fileInfo.LastWriteTimeUtc, snapshot);
        while (_cache.Count > 8)
        {
            _cache.Remove(_cache.Keys.First());
        }

        return snapshot;
    }

    private string? FindLatestLogPath()
    {
        try
        {
            return Directory.EnumerateFiles(_logDirectoryPath, "cli-*.log")
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => file.FullName)
                .FirstOrDefault();
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private IEnumerable<string> ReadTailLines(string path)
    {
        var fileInfo = new FileInfo(path);
        if (!fileInfo.Exists || fileInfo.Length == 0)
        {
            yield break;
        }

        var bytesToRead = (int)Math.Min(fileInfo.Length, MaxLogBytesToScan);
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

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            yield return line.TrimEnd('\r');
        }
    }

    private bool TryReadLogTimestamp(
        string line,
        DateTimeOffset nowUtc,
        out DateTimeOffset timestampUtc)
    {
        var match = LogTimestampRegex.Match(line);
        if (!match.Success ||
            !int.TryParse(match.Groups["month"].Value, out var month) ||
            !int.TryParse(match.Groups["day"].Value, out var day) ||
            !TimeSpan.TryParse(
                match.Groups["time"].Value,
                CultureInfo.InvariantCulture,
                out var time))
        {
            timestampUtc = default;
            return false;
        }

        var localNow = TimeZoneInfo.ConvertTime(nowUtc, _logTimeZone).DateTime;
        var year = localNow.Year;
        DateTime localTimestamp;
        try
        {
            localTimestamp = new DateTime(year, month, day).Add(time);
        }
        catch (ArgumentOutOfRangeException)
        {
            timestampUtc = default;
            return false;
        }

        if (localTimestamp - localNow > TimeSpan.FromDays(30))
        {
            localTimestamp = localTimestamp.AddYears(-1);
        }
        else if (localNow - localTimestamp > TimeSpan.FromDays(335))
        {
            localTimestamp = localTimestamp.AddYears(1);
        }

        timestampUtc = TimeZoneInfo.ConvertTimeToUtc(
                DateTime.SpecifyKind(localTimestamp, DateTimeKind.Unspecified),
                _logTimeZone)
            .ToUniversalTime();
        return true;
    }

    private sealed record ConfirmationRequest(
        DateTimeOffset RequestedAtUtc,
        int StepIndex,
        string ToolName);

    private sealed record ConfirmationResolution(
        string ConversationId,
        DateTimeOffset ResolvedAtUtc);

    private sealed record ConfirmationLogSnapshot(
        ConfirmationRequest? LatestRequest,
        IReadOnlyDictionary<int, ConfirmationResolution> Responses,
        IReadOnlyDictionary<string, DateTimeOffset> Cancellations);

    private sealed record CachedLogSnapshot(
        long Length,
        DateTime LastWriteTimeUtc,
        ConfirmationLogSnapshot Snapshot);
}
