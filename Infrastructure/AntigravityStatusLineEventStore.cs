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

        [JsonPropertyName("active_subagent_count")]
        public int? ActiveSubagentCount { get; init; }

        [JsonPropertyName("active_subagent_work_kinds")]
        public string[]? ActiveSubagentWorkKinds { get; init; }

        [JsonPropertyName("quota")]
        public Dictionary<string, EventQuota>? Quota { get; init; }

        [JsonPropertyName("plan_tier")]
        public string? PlanTier { get; init; }

        [JsonPropertyName("transcript_path")]
        public string? TranscriptPath { get; init; }

        [JsonPropertyName("artifact_directory_path")]
        public string? ArtifactDirectoryPath { get; init; }

        [JsonPropertyName("operation")]
        public EventOperation? Operation { get; init; }

        [JsonPropertyName("waiting_for_input")]
        public bool? WaitingForInput { get; init; }

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
                ActiveSubagentCount = NormalizeActiveSubagentCount(observation.ActiveSubagentCount),
                ActiveSubagentWorkKinds = NormalizeActiveSubagentWorkKinds(
                    observation.ActiveSubagentWorkKinds,
                    observation.ActiveSubagentCount),
                Quota = NormalizeQuotas(observation.Quotas),
                PlanTier = NormalizePlanTier(observation.PlanTier),
                Operation = observation.Operation is null
                    ? null
                    : new EventOperation(
                        ToWireOperationKind(observation.Operation.Kind),
                        observation.Operation.ToolName,
                        observation.Operation.Action,
                        observation.Operation.Summary,
                        observation.Operation.TargetPath,
                        observation.Operation.IsCompleted,
                        observation.Operation.ObservedAtUtc),
                WaitingForInput = observation.IsWaitingForInput ? true : null,
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
                    !IsValidActiveSubagentCount(parsed.ActiveSubagentCount) ||
                    !IsValidActiveSubagentWorkKinds(
                        parsed.ActiveSubagentCount,
                        parsed.ActiveSubagentWorkKinds) ||
                    !IsValidPlanTier(parsed.PlanTier) ||
                    !IsValidQuotas(parsed.Quota) ||
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
                    ContextWindow.TotalOutputTokens),
            ActiveSubagentCount,
            Quota?.Select(pair => new ProviderQuotaObservation(
                pair.Key,
                pair.Value.RemainingFraction,
                pair.Value.ResetAtUtc?.ToUniversalTime(),
                pair.Value.Window)).ToArray(),
            PlanTier)
        {
            TranscriptPath = TranscriptPath,
            ArtifactDirectoryPath = ArtifactDirectoryPath,
            Operation = Operation?.ToObservation(),
            ActiveSubagentWorkKinds = ParseActiveSubagentWorkKinds(ActiveSubagentWorkKinds),
            IsWaitingForInput = WaitingForInput == true
        };

        private static int? NormalizeActiveSubagentCount(int? count) =>
            IsValidActiveSubagentCount(count) ? count : null;

        private static string[]? NormalizeActiveSubagentWorkKinds(
            IReadOnlyList<SubagentWorkKind>? workKinds,
            int? activeCount)
        {
            if (workKinds is null || activeCount is null or <= 0)
            {
                return null;
            }

            if (workKinds.Count > activeCount.Value)
            {
                return null;
            }

            var normalized = workKinds
                .Where(IsKnownSubagentWorkKind)
                .Select(ToWireSubagentWorkKind)
                .ToArray();
            return normalized.Length == 0 ? null : normalized;
        }

        private static IReadOnlyList<SubagentWorkKind>? ParseActiveSubagentWorkKinds(
            IReadOnlyList<string>? workKinds) => workKinds is null
            ? null
            : workKinds.Select(ParseSubagentWorkKind).ToArray();

        private static bool IsValidActiveSubagentWorkKinds(
            int? activeCount,
            IReadOnlyList<string>? workKinds) =>
            workKinds is null ||
            workKinds.Count == 0 ||
            activeCount is > 0 &&
            workKinds.Count <= activeCount.Value &&
            workKinds.All(IsWireSubagentWorkKind);

        private static bool IsKnownSubagentWorkKind(SubagentWorkKind workKind) =>
            workKind is > SubagentWorkKind.Unknown and <= SubagentWorkKind.Coordinating;

        private static bool IsWireSubagentWorkKind(string workKind) => workKind is
            "thinking" or "editing" or "reading" or "researching" or "running_command" or "coordinating";

        private static string ToWireSubagentWorkKind(SubagentWorkKind workKind) => workKind switch
        {
            SubagentWorkKind.Thinking => "thinking",
            SubagentWorkKind.Editing => "editing",
            SubagentWorkKind.Reading => "reading",
            SubagentWorkKind.Researching => "researching",
            SubagentWorkKind.RunningCommand => "running_command",
            SubagentWorkKind.Coordinating => "coordinating",
            _ => string.Empty
        };

        private static SubagentWorkKind ParseSubagentWorkKind(string workKind) => workKind switch
        {
            "thinking" => SubagentWorkKind.Thinking,
            "editing" => SubagentWorkKind.Editing,
            "reading" => SubagentWorkKind.Reading,
            "researching" => SubagentWorkKind.Researching,
            "running_command" => SubagentWorkKind.RunningCommand,
            "coordinating" => SubagentWorkKind.Coordinating,
            _ => SubagentWorkKind.Unknown
        };

        private static string? NormalizePlanTier(string? planTier) =>
            IsValidPlanTier(planTier) ? planTier : null;

        private static Dictionary<string, EventQuota>? NormalizeQuotas(
            IReadOnlyList<ProviderQuotaObservation>? quotas)
        {
            if (quotas is null)
            {
                return null;
            }

            var normalized = quotas
                .Where(quota => IsValidQuota(quota.Id, quota.RemainingFraction, quota.ResetAtUtc, quota.Window))
                .GroupBy(quota => quota.Id, StringComparer.Ordinal)
                .Take(32)
                .ToDictionary(
                    group => group.Key,
                    group =>
                    {
                        var quota = group.First();
                        return new EventQuota(
                            quota.RemainingFraction,
                            quota.ResetAtUtc?.ToUniversalTime(),
                            quota.Window);
                    },
                    StringComparer.Ordinal);

            return normalized.Count == 0 ? null : normalized;
        }

        private static bool IsValidActiveSubagentCount(int? count) =>
            count is null || count is >= 0 and <= ProviderObservation.MaxActiveSubagentCount;

        private static bool IsValidPlanTier(string? planTier) =>
            planTier is null || IsSafeText(planTier, 128);

        private static bool IsValidQuotas(Dictionary<string, EventQuota>? quotas)
        {
            return quotas is null ||
                quotas.Count <= 32 &&
                quotas.All(pair =>
                    IsSafeQuotaId(pair.Key) &&
                    IsValidQuota(
                        pair.Key,
                        pair.Value.RemainingFraction,
                        pair.Value.ResetAtUtc,
                        pair.Value.Window));
        }

        private static bool IsValidQuota(
            string id,
            decimal remainingFraction,
            DateTimeOffset? resetAtUtc,
            string? window)
        {
            return IsSafeQuotaId(id) &&
                remainingFraction is >= 0m and <= 1m &&
                (resetAtUtc is null || resetAtUtc.Value != default) &&
                (window is null || IsSafeText(window, 64));
        }

        private static bool IsSafeQuotaId(string value) =>
            IsSafeText(value, 128) &&
            !value.Contains('/', StringComparison.Ordinal) &&
            !value.Contains('\\', StringComparison.Ordinal);

        private static bool IsSafeText(string? value, int maxLength) =>
            !string.IsNullOrWhiteSpace(value) &&
            value.Length <= maxLength &&
            value.All(character => !char.IsControl(character));

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

        private static string ToWireOperationKind(CodexOperationKind kind) => kind switch
        {
            CodexOperationKind.Read => "read",
            CodexOperationKind.Edit => "edit",
            CodexOperationKind.Create => "create",
            CodexOperationKind.Delete => "delete",
            CodexOperationKind.Command => "command",
            CodexOperationKind.Research => "research",
            _ => "unknown"
        };

        private static ProviderExecutionMode ParseExecutionMode(string? mode) => mode?.ToLowerInvariant() switch
        {
            "planning" => ProviderExecutionMode.Planning,
            "fast" => ProviderExecutionMode.Fast,
            _ => ProviderExecutionMode.Unknown
        };

        public sealed record EventOperation(
            [property: JsonPropertyName("kind")] string? Kind,
            [property: JsonPropertyName("tool_name")] string? ToolName,
            [property: JsonPropertyName("action")] string? Action,
            [property: JsonPropertyName("summary")] string? Summary,
            [property: JsonPropertyName("target_path")] string? TargetPath,
            [property: JsonPropertyName("is_completed")] bool IsCompleted,
            [property: JsonPropertyName("observed_at_utc")] DateTimeOffset? ObservedAtUtc)
        {
            public ProviderOperationObservation ToObservation() => new(
                ParseOperationKind(Kind),
                ToolName,
                Action,
                Summary,
                TargetPath,
                IsCompleted,
                ObservedAtUtc?.ToUniversalTime());

            private static CodexOperationKind ParseOperationKind(string? kind) => kind?.ToLowerInvariant() switch
            {
                "read" => CodexOperationKind.Read,
                "edit" => CodexOperationKind.Edit,
                "create" => CodexOperationKind.Create,
                "delete" => CodexOperationKind.Delete,
                "command" => CodexOperationKind.Command,
                "research" => CodexOperationKind.Research,
                _ => CodexOperationKind.Unknown
            };
        }

        private static ProviderAgentState ParseAgentState(string? state) => state?.ToLowerInvariant() switch
        {
            "idle" => ProviderAgentState.Idle,
            "thinking" => ProviderAgentState.Thinking,
            "working" => ProviderAgentState.Working,
            "tool_use" => ProviderAgentState.ToolUse,
            "initializing" => ProviderAgentState.Initializing,
            _ => ProviderAgentState.Unknown
        };

        public sealed record EventQuota(
            [property: JsonPropertyName("remaining_fraction")] decimal RemainingFraction,
            [property: JsonPropertyName("reset_time")] DateTimeOffset? ResetAtUtc,
            [property: JsonPropertyName("window")] string? Window);

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
