using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexDiscordPresence;

internal sealed class AntigravityStatusLineEventStore
{
    internal const int MaxEventFileBytes = 1024 * 1024;
    internal const int MaxEventCount = 64;

    private const int MaxEventLineBytes = 256 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = false
    };
    private readonly object _sync = new();
    private readonly string _eventFilePath;

    internal AntigravityStatusLineEventStore(string eventFilePath)
    {
        if (string.IsNullOrWhiteSpace(eventFilePath))
        {
            throw new ArgumentException("An event file path is required.", nameof(eventFilePath));
        }

        _eventFilePath = Path.GetFullPath(eventFilePath);
    }

    internal bool TryAppend(ProviderObservation observation, string? localProjectPath = null)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var document = AntigravityStatusLineEventDocument.FromObservation(
            observation,
            CreateProjectKey(localProjectPath));
        var line = JsonSerializer.Serialize(document, JsonOptions);
        var lineBytes = Encoding.UTF8.GetBytes(line);
        if (lineBytes.Length > MaxEventLineBytes || lineBytes.Length + 1 > MaxEventFileBytes)
        {
            return false;
        }

        lock (_sync)
        {
            try
            {
                var lines = ReadValidLines();
                lines.Add(line);
                TrimToBounds(lines);
                WriteAtomically(lines);
                return true;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    internal bool TryReadLatest(out ProviderObservation? observation)
    {
        return TryReadLatestCore(null, out observation);
    }

    internal bool TryReadLatest(string? localProjectPath, out ProviderObservation? observation)
    {
        return TryReadLatestCore(CreateProjectKey(localProjectPath), out observation);
    }

    internal bool TryReadLatestByConversation(
        string? localProjectPath,
        out IReadOnlyList<ProviderObservation> observations)
    {
        observations = Array.Empty<ProviderObservation>();
        var projectKey = CreateProjectKey(localProjectPath);

        List<string> lines;
        try
        {
            lines = ReadValidLines();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        var latestByConversation = new List<ProviderObservation>();
        var conversationKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines.AsEnumerable().Reverse())
        {
            if (!AntigravityStatusLineEventDocument.TryParse(line, out var document) ||
                (projectKey is not null && !string.Equals(
                    projectKey,
                    document.ProjectKey,
                    StringComparison.Ordinal)))
            {
                continue;
            }

            var conversationKey = string.IsNullOrWhiteSpace(document.ConversationId)
                ? "<unknown>"
                : document.ConversationId.Trim();
            if (!conversationKeys.Add(conversationKey))
            {
                continue;
            }

            latestByConversation.Add(document.ToObservation());
        }

        observations = latestByConversation;
        return latestByConversation.Count > 0;
    }

    private bool TryReadLatestCore(string? projectKey, out ProviderObservation? observation)
    {
        observation = null;

        List<string> lines;
        try
        {
            lines = ReadValidLines();
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        foreach (var line in lines.AsEnumerable().Reverse())
        {
            if (!AntigravityStatusLineEventDocument.TryParse(line, out var document) ||
                (projectKey is not null && !string.Equals(
                    projectKey,
                    document.ProjectKey,
                    StringComparison.Ordinal)))
            {
                continue;
            }

            observation = document.ToObservation();
            return true;
        }

        return false;
    }

    private List<string> ReadValidLines()
    {
        if (!File.Exists(_eventFilePath))
        {
            return [];
        }

        var fileInfo = new FileInfo(_eventFilePath);
        if (fileInfo.Length == 0)
        {
            return [];
        }

        var bytesToRead = (int)Math.Min(fileInfo.Length, MaxEventFileBytes);
        var bytes = new byte[bytesToRead];
        using (var stream = new FileStream(
                   _eventFilePath,
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

        var validLines = new List<string>();
        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var normalizedLine = line.TrimEnd('\r');
            if (Encoding.UTF8.GetByteCount(normalizedLine) <= MaxEventLineBytes &&
                AntigravityStatusLineEventDocument.TryParse(normalizedLine, out _))
            {
                validLines.Add(normalizedLine);
            }
        }

        return validLines;
    }

    private static void TrimToBounds(List<string> lines)
    {
        while (lines.Count > MaxEventCount || GetFileByteCount(lines) > MaxEventFileBytes)
        {
            lines.RemoveAt(0);
        }
    }

    private static int GetFileByteCount(IEnumerable<string> lines)
    {
        var byteCount = 0;
        foreach (var line in lines)
        {
            byteCount += Encoding.UTF8.GetByteCount(line) + 1;
        }

        return byteCount;
    }

    private void WriteAtomically(IReadOnlyList<string> lines)
    {
        var directory = Path.GetDirectoryName(_eventFilePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _eventFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            using (var writer = new StreamWriter(
                       stream,
                       new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                foreach (var line in lines)
                {
                    writer.WriteLine(line);
                }

                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, _eventFilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string? CreateProjectKey(string? localProjectPath)
    {
        if (string.IsNullOrWhiteSpace(localProjectPath))
        {
            return null;
        }

        try
        {
            var normalizedPath = Path.GetFullPath(localProjectPath);
            var root = Path.GetPathRoot(normalizedPath);
            if (!string.Equals(normalizedPath, root, StringComparison.OrdinalIgnoreCase))
            {
                normalizedPath = normalizedPath.TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);
            }

            var bytes = Encoding.UTF8.GetBytes(normalizedPath.ToUpperInvariant());
            return Convert.ToHexString(SHA256.HashData(bytes));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private sealed record AntigravityStatusLineEventDocument
    {
        [JsonPropertyName("schema_version")]
        public int SchemaVersion { get; init; }

        [JsonPropertyName("source")]
        public string? Source { get; init; }

        [JsonPropertyName("observed_at_utc")]
        public DateTimeOffset ObservedAtUtc { get; init; }

        [JsonPropertyName("agent_state")]
        public string? AgentState { get; init; }

        [JsonPropertyName("model")]
        public EventModel? Model { get; init; }

        [JsonPropertyName("workspace")]
        public EventWorkspace? Workspace { get; init; }

        [JsonPropertyName("conversation_id")]
        public string? ConversationId { get; init; }

        [JsonPropertyName("execution_mode")]
        public string? ExecutionMode { get; init; }

        [JsonPropertyName("context_window")]
        public EventContextWindow? ContextWindow { get; init; }

        [JsonPropertyName("project_key")]
        public string? ProjectKey { get; init; }

        public static AntigravityStatusLineEventDocument FromObservation(
            ProviderObservation observation,
            string? projectKey) => new()
            {
                SchemaVersion = 1,
                Source = "antigravity",
                ObservedAtUtc = observation.ObservedAtUtc.ToUniversalTime(),
                AgentState = ToWireAgentState(observation.AgentState),
                Model = observation.Model is null
                    ? null
                    : new EventModel(observation.Model.Id, observation.Model.DisplayName),
                Workspace = observation.Workspace is null
                    ? null
                    : new EventWorkspace(
                        observation.Workspace.WorkspaceName,
                        observation.Workspace.ProjectName),
                ConversationId = observation.ConversationId,
                ExecutionMode = ToWireExecutionMode(observation.ExecutionMode),
                ContextWindow = observation.ContextWindow is null
                    ? null
                    : new EventContextWindow(
                        observation.ContextWindow.TotalInputTokens,
                        observation.ContextWindow.TotalOutputTokens),
                ProjectKey = projectKey
            };

        public static bool TryParse(
            string line,
            out AntigravityStatusLineEventDocument document)
        {
            document = null!;
            try
            {
                var parsed = JsonSerializer.Deserialize<AntigravityStatusLineEventDocument>(
                    line,
                    JsonOptions);
                if (parsed is null ||
                    parsed.SchemaVersion != 1 ||
                    !string.Equals(parsed.Source, "antigravity", StringComparison.Ordinal) ||
                    parsed.ObservedAtUtc == default ||
                    !IsSafeProjectKey(parsed.ProjectKey))
                {
                    return false;
                }

                document = parsed;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        public ProviderObservation ToObservation() => new(
            ProviderObservationSource.AntigravityCli,
            ObservedAtUtc.ToUniversalTime(),
            ParseAgentState(AgentState),
            Model is null || (Model.Id is null && Model.DisplayName is null)
                ? null
                : new ProviderModelObservation(Model.Id, Model.DisplayName),
            Workspace is null || (Workspace.WorkspaceName is null && Workspace.ProjectName is null)
                ? null
                : new ProviderWorkspaceObservation(
                    Workspace.WorkspaceName,
                    Workspace.WorkspaceName,
                    Workspace.ProjectName),
            ConversationId,
            ParseExecutionMode(ExecutionMode),
            ContextWindow is null
                ? null
                : new ProviderContextWindowObservation(
                    ContextWindow.TotalInputTokens,
                    ContextWindow.TotalOutputTokens));

        private static string ToWireAgentState(ProviderAgentState state) => state switch
        {
            ProviderAgentState.Idle => "idle",
            ProviderAgentState.Thinking => "thinking",
            ProviderAgentState.Working => "working",
            ProviderAgentState.ToolUse => "tool_use",
            ProviderAgentState.Initializing => "initializing",
            _ => "unknown"
        };

        private static string? ToWireExecutionMode(ProviderExecutionMode mode) => mode switch
        {
            ProviderExecutionMode.Planning => "planning",
            ProviderExecutionMode.Fast => "fast",
            _ => null
        };

        private static ProviderExecutionMode ParseExecutionMode(string? mode) => mode?.ToLowerInvariant() switch
        {
            "planning" => ProviderExecutionMode.Planning,
            "fast" => ProviderExecutionMode.Fast,
            _ => ProviderExecutionMode.Unknown
        };

        private static ProviderAgentState ParseAgentState(string? state) => state?.ToLowerInvariant() switch
        {
            "idle" => ProviderAgentState.Idle,
            "thinking" => ProviderAgentState.Thinking,
            "working" => ProviderAgentState.Working,
            "tool_use" => ProviderAgentState.ToolUse,
            "initializing" => ProviderAgentState.Initializing,
            _ => ProviderAgentState.Unknown
        };

        private static bool IsSafeProjectKey(string? projectKey) =>
            projectKey is null ||
            (projectKey.Length == 64 && projectKey.All(Uri.IsHexDigit));
    }

    private sealed record EventModel(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("display_name")] string? DisplayName);

    private sealed record EventWorkspace(
        [property: JsonPropertyName("workspace_name")] string? WorkspaceName,
        [property: JsonPropertyName("project_name")] string? ProjectName);

    private sealed record EventContextWindow(
        [property: JsonPropertyName("total_input_tokens")] long? TotalInputTokens,
        [property: JsonPropertyName("total_output_tokens")] long? TotalOutputTokens);
}
