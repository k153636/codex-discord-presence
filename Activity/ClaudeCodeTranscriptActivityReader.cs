using System.Diagnostics;
using System.Text.Json;

namespace CodexDiscordPresence;

// Bootstrap already-running sessions without restarting Claude or modifying their transcripts.
internal sealed class ClaudeCodeTranscriptActivityReader(string projectsDirectory)
{
    private DateTimeOffset _lastScanUtc;
    private IReadOnlyList<ClaudeCodeSessionObservation> _observations = [];
    private readonly Dictionary<string, (long Length, DateTime LastWriteUtc, ClaudeCodeSessionObservation? Observation)> _cache = new(StringComparer.OrdinalIgnoreCase);

    internal ClaudeCodeSessionObservation? Select(string projectPath, DateTimeOffset nowUtc, TimeSpan freshness)
    {
        if (nowUtc - _lastScanUtc >= TimeSpan.FromSeconds(3))
        {
            _lastScanUtc = nowUtc;
            _observations = Scan();
        }
        return _observations
            .Where(observation => ClaudeCodeObservationStore.IsEligible(observation, observation.ProjectPath, nowUtc, freshness))
            .OrderByDescending(observation => new ProviderWorkspaceObservation(observation.ProjectPath, null, null).MatchesProjectPath(projectPath))
            .ThenByDescending(observation => observation.ObservedAtUtc)
            .FirstOrDefault();
    }

    private IReadOnlyList<ClaudeCodeSessionObservation> Scan()
    {
        if (!Directory.Exists(projectsDirectory))
        {
            return [];
        }
        try
        {
            var files = Directory.EnumerateDirectories(projectsDirectory)
                .SelectMany(directory => Directory.EnumerateFiles(directory, "*.jsonl", SearchOption.TopDirectoryOnly))
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(20)
                .ToArray();
            var currentPaths = files.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var oldPath in _cache.Keys.Where(path => !currentPaths.Contains(path)).ToArray())
            {
                _cache.Remove(oldPath);
            }
            return files.Select(ReadCached)
                .OfType<ClaudeCodeSessionObservation>()
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private ClaudeCodeSessionObservation? ReadCached(FileInfo file)
    {
        if (_cache.TryGetValue(file.FullName, out var cached) &&
            cached.Length == file.Length && cached.LastWriteUtc == file.LastWriteTimeUtc)
        {
            return cached.Observation;
        }
        var observation = Read(file.FullName);
        _cache[file.FullName] = (file.Length, file.LastWriteTimeUtc, observation);
        return observation;
    }

    internal static ClaudeCodeSessionObservation? Read(string path)
    {
        ClaudeCodeSessionObservation? state = null;
        try
        {
            foreach (var line in ClaudeCodeTranscriptTail.ReadLines(path))
            {
                state = ApplyLine(state, line, path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
        return state is null ? null : state with { FromTranscript = true };
    }

    private static ClaudeCodeSessionObservation? ApplyLine(ClaudeCodeSessionObservation? state, string line, string path)
    {
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (root.TryGetProperty("isSidechain", out var sidechain) && sidechain.ValueKind == JsonValueKind.True ||
                ClaudeCodeHookParser.Text(root, "sessionId") is not { } sessionId ||
                ClaudeCodeHookParser.Text(root, "cwd", 4096) is not { } cwd || !Path.IsPathFullyQualified(cwd) ||
                ClaudeCodeHookParser.Text(root, "timestamp") is not { } timestamp ||
                !DateTimeOffset.TryParse(timestamp, out var observedAt))
            {
                return state;
            }
            var baseEvent = new ClaudeCodeHookEvent(sessionId, cwd, "UserPromptSubmit", observedAt, TranscriptPath: path);
            var type = ClaudeCodeHookParser.Text(root, "type");
            if (type == "system" && ClaudeCodeHookParser.Text(root, "subtype") == "turn_duration")
            {
                return ClaudeCodeSessionObservation.Apply(state, baseEvent with { EventName = "Stop" });
            }
            if (type is not ("assistant" or "user") || !root.TryGetProperty("message", out var message))
            {
                return state;
            }
            if (root.TryGetProperty("isMeta", out var meta) && meta.ValueKind == JsonValueKind.True)
            {
                return state;
            }
            if (type == "assistant")
            {
                baseEvent = baseEvent with { Model = ClaudeCodeHookParser.Text(message, "model") };
            }
            if (!message.TryGetProperty("content", out var content) || content.ValueKind != JsonValueKind.Array)
            {
                return type == "user" && content.ValueKind == JsonValueKind.String
                    ? ClaudeCodeSessionObservation.Apply(state, baseEvent) : state;
            }
            foreach (var item in content.EnumerateArray())
            {
                var contentType = ClaudeCodeHookParser.Text(item, "type");
                if (type == "assistant" && contentType == "tool_use")
                {
                    var file = item.TryGetProperty("input", out var input)
                        ? ClaudeCodeHookParser.Text(input, "file_path", 4096) ?? ClaudeCodeHookParser.Text(input, "notebook_path", 4096)
                        : null;
                    state = ClaudeCodeSessionObservation.Apply(state, baseEvent with
                    {
                        EventName = "PreToolUse",
                        ToolName = ClaudeCodeHookParser.Text(item, "name"),
                        ToolUseId = ClaudeCodeHookParser.Text(item, "id"),
                        FileName = ClaudeCodeHookParser.SafeText(file?.Replace('\\', '/').Split('/').LastOrDefault())
                    });
                }
                else if (type == "user" && contentType == "tool_result")
                {
                    state = ClaudeCodeSessionObservation.Apply(state, baseEvent with
                    {
                        EventName = "PostToolUse", ToolUseId = ClaudeCodeHookParser.Text(item, "tool_use_id")
                    });
                }
                else if (contentType is "thinking" or "text" && type == "assistant")
                {
                    // Message bodies and private reasoning are deliberately not inspected.
                    state = ClaudeCodeSessionObservation.Apply(state, baseEvent with { EventName = "PostToolUse" });
                }
                else if (type == "user" && contentType == "text")
                {
                    state = ClaudeCodeSessionObservation.Apply(state, baseEvent);
                }
            }
            if (type == "assistant" && ClaudeCodeHookParser.Text(message, "stop_reason") == "end_turn")
            {
                state = ClaudeCodeSessionObservation.Apply(state, baseEvent with { EventName = "Stop" });
            }
            return state;
        }
        catch (JsonException)
        {
            return state;
        }
    }

    internal static bool IsNativeProcessRunning()
    {
        var processes = Process.GetProcessesByName("claude");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
