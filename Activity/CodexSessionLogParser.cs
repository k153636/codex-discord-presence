using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class CodexSessionLogParser
{
    private const int MaxHeaderLinesToScan = 256;
    private const long MaxTailBytesToScan = 2 * 1024 * 1024;
    private static readonly System.Text.RegularExpressions.Regex NestedShellCommandRegex = new(
        @"(?:tools\.)?(?:exec_command|shell_command|run_command)\s*\(\s*\{.*?(?:[""']?)(?:cmd|command)(?:[""']?)\s*:\s*""(?<value>(?:\\.|[^""\\])*)""",
        System.Text.RegularExpressions.RegexOptions.IgnoreCase |
        System.Text.RegularExpressions.RegexOptions.Singleline |
        System.Text.RegularExpressions.RegexOptions.Compiled);

    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;
    private readonly Dictionary<string, CachedSessionInspection> _sessionCache = new(StringComparer.OrdinalIgnoreCase);

    public CodexSessionLogParser(CodexDetectionOptions options, PresenceTemplateOptions presenceOptions)
    {
        _options = options;
        _presenceOptions = presenceOptions;
    }

    public SessionInspection? InspectRecentSessions(string? projectPath)
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        try
        {
            var normalizedProjectPath = string.IsNullOrWhiteSpace(projectPath)
                ? null
                : NormalizePath(projectPath);

            var files = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RecentSessionFilesToScan))
                .ToArray();

            var candidates = new List<SessionInspectionCandidate>(files.Length);

            foreach (var file in files)
            {
                var inspection = GetCachedInspection(file, normalizedProjectPath);
                candidates.Add(new SessionInspectionCandidate(inspection, file.LastWriteTimeUtc));
            }

            return SelectInspection(candidates, normalizedProjectPath);
        }
        catch
        {
            return null;
        }
    }

    public string? GetLatestObservedProjectPath()
    {
        var resolvedPath = _options.GetResolvedHomePath();
        var sessionsPath = Path.Combine(resolvedPath, "sessions");
        if (!Directory.Exists(sessionsPath))
        {
            return null;
        }

        try
        {
            var files = Directory
                .EnumerateFiles(sessionsPath, "*.jsonl", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path))
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(Math.Max(1, _options.RecentSessionFilesToScan));

            var candidates = new List<SessionInspectionCandidate>();
            foreach (var file in files)
            {
                var inspection = GetCachedInspection(file, normalizedProjectPath: null);
                candidates.Add(new SessionInspectionCandidate(inspection, file.LastWriteTimeUtc));
            }

            return SelectLatestObservedProjectPath(candidates);
        }
        catch
        {
            return null;
        }
    }

    private SessionInspection GetCachedInspection(FileInfo file, string? normalizedProjectPath)
    {
        var cacheKey = file.FullName;
        if (_sessionCache.TryGetValue(cacheKey, out var cached) &&
            cached.Length == file.Length &&
            cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
        {
            return ApplyProjectMatch(cached.Inspection, normalizedProjectPath);
        }

        var inspection = AnalyzeSessionFile(file.FullName);
        _sessionCache[cacheKey] = new CachedSessionInspection(
            file.Length,
            file.LastWriteTimeUtc,
            inspection);
        return ApplyProjectMatch(inspection, normalizedProjectPath);
    }

    private SessionInspection AnalyzeSessionFile(string path)
    {
        var hasProjectPath = false;
        string? latestProjectPath = null;
        var hasTaskStarted = false;
        var hasTaskCompleted = false;
        DateTime? lastTaskStartedAt = null;
        DateTime? lastTaskCompletedAt = null;
        DateTime? lastObservedAt = null;
        DateTime? lastShellCommandAt = null;
        RunningCommandKind lastRunningCommandKind = RunningCommandKind.Unknown;
        string? lastRunningCommandName = null;
        string? lastDirectToolFilePath = null;
        DateTime? lastDirectToolFileAt = null;
        string? collaborationMode = null;
        var pendingShellCommands = new HashSet<string>(StringComparer.Ordinal);
        var completedShellCommands = new HashSet<string>(StringComparer.Ordinal);
        var activityEvents = new List<CodexActivityEvent>();
        var sequence = 0L;
        string? runningCommandReason = null;

        try
        {
            foreach (var line in ReadSessionLines(path))
            {
                if (!line.Contains("\"payload\"", StringComparison.Ordinal))
                {
                    continue;
                }

                using var document = JsonDocument.Parse(line);
                if (!document.RootElement.TryGetProperty("payload", out var payload))
                {
                    continue;
                }

                var timestamp = TryGetTimestamp(document.RootElement);
                if (timestamp.HasValue)
                {
                    lastObservedAt = timestamp;
                }

                if (TryGetString(payload, "cwd", out var cwd))
                {
                    hasProjectPath = true;
                    latestProjectPath = cwd;
                }

                var payloadType = TryGetString(payload, "type", out var type) ? type : null;

                sequence++;
                if (TryNormalizeActivityEvent(
                        sequence,
                        timestamp ?? DateTime.UtcNow,
                        payload,
                        payloadType,
                        latestProjectPath,
                        out var activityEvent))
                {
                    activityEvents.Add(activityEvent);
                }

                var directToolFilePath = TryGetDirectToolFilePath(payload, payloadType, latestProjectPath);
                if (directToolFilePath is not null && timestamp.HasValue &&
                    (!lastDirectToolFileAt.HasValue || timestamp >= lastDirectToolFileAt))
                {
                    lastDirectToolFilePath = directToolFilePath;
                    lastDirectToolFileAt = timestamp;
                }

                if (payloadType is "task_started" || line.Contains("\"task_started\"", StringComparison.Ordinal))
                {
                    hasTaskStarted = true;
                    if (timestamp.HasValue)
                    {
                        lastTaskStartedAt = timestamp;
                    }

                    var mode = TryGetCollaborationMode(payload);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        collaborationMode = mode;
                    }
                }

                if (payloadType is "task_complete" || line.Contains("\"task_complete\"", StringComparison.Ordinal))
                {
                    hasTaskCompleted = true;
                    if (timestamp.HasValue)
                    {
                        lastTaskCompletedAt = timestamp;
                    }
                }

                if (payloadType is "turn_context")
                {
                    var mode = TryGetCollaborationMode(payload);
                    if (!string.IsNullOrWhiteSpace(mode))
                    {
                        collaborationMode = mode;
                    }
                }

                if (payloadType is "function_call" &&
                    TryGetString(payload, "name", out var functionName) &&
                    string.Equals(functionName, "shell_command", StringComparison.OrdinalIgnoreCase) &&
                    TryGetString(payload, "call_id", out var callId))
                {
                    pendingShellCommands.Add(callId);
                    if (TryGetShellCommandText(payload, out var commandText))
                    {
                        if (IsPassiveShellCommand(commandText))
                        {
                            pendingShellCommands.Remove(callId);
                            continue;
                        }

                        var commandKind = ClassifyShellCommand(commandText);
                        var commandName = ExtractCommandName(commandText);
                        var displayCommandName = commandName ?? DescribeRunningCommandKind(commandKind);
                        if (!lastShellCommandAt.HasValue || timestamp >= lastShellCommandAt)
                        {
                            lastShellCommandAt = timestamp;
                            lastRunningCommandKind = commandKind ?? RunningCommandKind.Unknown;
                            lastRunningCommandName = commandName;
                            runningCommandReason = commandKind is null
                                ? "pending shell_command function call in session log"
                                : $"shell_command looks like {displayCommandName}";
                        }
                    }
                }

                if (payloadType is "function_call_output" &&
                    TryGetString(payload, "call_id", out var outputCallId))
                {
                    completedShellCommands.Add(outputCallId);
                }

                if (runningCommandReason is null &&
                    payloadType is "function_call" &&
                    TryGetString(payload, "name", out var callName) &&
                    string.Equals(callName, "shell_command", StringComparison.OrdinalIgnoreCase))
                {
                    runningCommandReason = "pending shell_command function call in session log";
                }
            }
        }
        catch
        {
            // Fall through with what we were able to infer.
        }

        var hasRunningCommand = pendingShellCommands.Except(completedShellCommands).Any();
        if (hasRunningCommand && runningCommandReason is null)
        {
            runningCommandReason = "pending shell_command function call in session log";
        }

        return new SessionInspection(
            hasProjectPath,
            false,
            hasTaskStarted,
            hasTaskCompleted,
            lastTaskStartedAt,
            lastTaskCompletedAt,
            lastObservedAt,
            collaborationMode,
            hasRunningCommand,
            runningCommandReason,
            null)
        {
            ProjectPath = latestProjectPath,
            LastShellCommandAt = lastShellCommandAt,
            LastRunningCommandKind = lastRunningCommandKind,
            LastRunningCommandName = lastRunningCommandName,
            LastDirectToolFilePath = lastDirectToolFilePath,
            LastDirectToolFileAt = lastDirectToolFileAt,
            ActivityEvents = activityEvents
        };
    }

    private static SessionInspection ApplyProjectMatch(
        SessionInspection inspection,
        string? normalizedProjectPath)
    {
        var matchesProject = normalizedProjectPath is not null &&
            !string.IsNullOrWhiteSpace(inspection.ProjectPath) &&
            NormalizePath(inspection.ProjectPath) == normalizedProjectPath;
        return inspection with { MatchesProject = matchesProject };
    }

    private static IEnumerable<string> ReadSessionLines(string path)
    {
        var fileLength = new FileInfo(path).Length;
        if (fileLength <= MaxTailBytesToScan)
        {
            return ReadLinesFromOffset(path, 0, includePartialFirstLine: true);
        }

        return ReadLargeSessionLines(path, fileLength);
    }

    private static IEnumerable<string> ReadLargeSessionLines(string path, long fileLength)
    {
        foreach (var line in ReadLinesFromHead(path))
        {
            yield return line;
        }

        var tailOffset = FindLineStart(path, Math.Max(0, fileLength - MaxTailBytesToScan));
        foreach (var line in ReadLinesFromOffset(path, tailOffset, includePartialFirstLine: true))
        {
            yield return line;
        }
    }

    private static IEnumerable<string> ReadLinesFromHead(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        for (var lineNumber = 0; lineNumber < MaxHeaderLinesToScan; lineNumber++)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                yield break;
            }

            yield return line;
        }
    }

    private static IEnumerable<string> ReadLinesFromOffset(
        string path,
        long offset,
        bool includePartialFirstLine)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Max(0, offset), SeekOrigin.Begin);
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        if (!includePartialFirstLine && offset > 0)
        {
            reader.ReadLine();
        }

        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            yield return line;
        }
    }

    private static long FindLineStart(string path, long offset)
    {
        if (offset <= 0)
        {
            return 0;
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var buffer = new byte[8192];
        var searchEnd = offset;
        while (searchEnd > 0)
        {
            var blockStart = Math.Max(0, searchEnd - buffer.Length);
            var blockLength = (int)(searchEnd - blockStart);
            stream.Seek(blockStart, SeekOrigin.Begin);
            var bytesRead = stream.Read(buffer, 0, blockLength);
            for (var index = bytesRead - 1; index >= 0; index--)
            {
                if (buffer[index] == (byte)'\n')
                {
                    return blockStart + index + 1;
                }
            }

            searchEnd = blockStart;
        }

        return 0;
    }

    private static bool TryNormalizeActivityEvent(
        long sequence,
        DateTime timestampUtc,
        JsonElement payload,
        string? payloadType,
        string? projectPath,
        out CodexActivityEvent activityEvent)
    {
        activityEvent = new CodexActivityEvent
        {
            Sequence = sequence,
            TimestampUtc = timestampUtc,
            Kind = CodexActivityEventKind.ContextUpdated,
            TurnId = TryGetFirstString(payload, "turn_id", "turnId"),
            CallId = TryGetFirstString(payload, "call_id", "callId", "id"),
            Source = CodexActivitySource.SessionLog
        };

        switch (payloadType?.Trim().ToLowerInvariant())
        {
            case "task_started":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.TurnStarted,
                    Reason = "task_started"
                };
                return true;

            case "task_complete":
            case "turn_completed":
                activityEvent = activityEvent with
                {
                    Kind = ResolveTerminalEventKind(payload),
                    Reason = "turn completed"
                };
                return true;

            case "turn_aborted":
            case "task_aborted":
            case "turn_interrupted":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.TurnInterrupted,
                    Reason = "turn interrupted"
                };
                return true;

            case "turn_failed":
            case "task_failed":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.TurnFailed,
                    Reason = "turn failed"
                };
                return true;

            case "reasoning":
            case "agent_reasoning":
            case "agent_message":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.Reasoning,
                    ThinkingSummary = TryGetThinkingSummary(payload, payloadType),
                    Reason = payloadType
                };
                return true;

            case "item_completed":
                if (TryGetAgentLifecycleEvent(payload, out var agentEventKind, out var agentThreadIds))
                {
                    activityEvent = activityEvent with
                    {
                        Kind = agentEventKind,
                        AgentThreadIds = agentThreadIds,
                        Reason = agentEventKind == CodexActivityEventKind.AgentStarted
                            ? "subagent started"
                            : "subagent completed"
                    };
                    return true;
                }

                if (!IsReasoningItem(payload))
                {
                    return false;
                }

                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.Reasoning,
                    ThinkingSummary = TryGetThinkingSummary(payload, payloadType),
                    Reason = "reasoning item completed"
                };
                return true;

            case "request_user_input":
            case "permission_request":
            case "permission_requested":
            case "input_request":
            case "approval_request":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.InputRequested,
                    Reason = "input or permission requested"
                };
                return true;

            case "input_resolved":
            case "permission_resolved":
            case "approval_resolved":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.InputResolved,
                    Reason = "input or permission resolved"
                };
                return true;

            case "turn_context":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.ContextUpdated,
                    Reason = "turn context updated"
                };
                return true;

            case "web_search_end":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.OperationCompleted,
                    OperationKind = CodexOperationKind.Research,
                    Reason = "web search completed"
                };
                return true;

            case "function_call":
            case "custom_tool_call":
            {
                var rawToolName = TryGetString(payload, "name");
                var toolInput = TryGetString(payload, "input") ?? TryGetString(payload, "arguments");
                var nestedMcpToolName = ExtractNestedMcpToolName(toolInput);
                var toolName = nestedMcpToolName ?? rawToolName;
                var isMcpOperation = nestedMcpToolName is not null || IsMcpToolName(rawToolName);
                if (IsInputTool(rawToolName))
                {
                    activityEvent = activityEvent with
                    {
                        Kind = CodexActivityEventKind.InputRequested,
                        Reason = $"{rawToolName} requested input"
                    };
                    return true;
                }

                var commandText = TryGetShellCommandText(payload, out var shellCommand)
                    ? shellCommand
                    : null;
                if (!string.IsNullOrWhiteSpace(commandText) && IsPassiveShellCommand(commandText))
                {
                    return false;
                }

                var targetPaths = TryGetDirectToolFilePaths(payload, payloadType, projectPath);
                var shellMutation = TryGetShellMutation(commandText, projectPath);
                if (shellMutation is not null)
                {
                    targetPaths = shellMutation.Value.TargetPaths;
                }

                var operationKind = shellMutation?.OperationKind ??
                    ClassifyOperationKind(toolName, commandText, targetPaths, toolInput);
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.OperationStarted,
                    OperationKind = operationKind,
                    TargetPaths = targetPaths,
                    CommandKind = ClassifyShellCommand(commandText ?? "") ?? RunningCommandKind.Unknown,
                    CommandName = commandText is null ? null : ExtractCommandName(commandText),
                    IsMcpOperation = isMcpOperation,
                    McpServerName = McpServerNameFormatter.ExtractServerName(toolName),
                    Reason = isMcpOperation
                        ? $"MCP {McpServerNameFormatter.ExtractToolName(toolName) ?? toolName ?? "operation"} operation started"
                        : string.IsNullOrWhiteSpace(toolName)
                        ? "tool operation started"
                        : $"{toolName} operation started"
                };
                return true;
            }

            case "function_call_output":
            case "custom_tool_call_output":
            case "mcp_tool_call_end":
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.OperationCompleted,
                    IsMcpOperation = payloadType is "mcp_tool_call_end",
                    McpServerName = payloadType is "mcp_tool_call_end"
                        ? TryGetInvocationMcpServerName(payload)
                        : null,
                    TargetPaths = TryGetDirectToolFilePaths(payload, payloadType, projectPath),
                    Reason = "tool operation completed"
                };
                return true;

            case "mcp_tool_call":
            {
                var toolName = TryGetInvocationToolName(payload);
                var mcpServerName = TryGetInvocationMcpServerName(payload);
                var targetPaths = TryGetDirectToolFilePaths(payload, payloadType, projectPath);
                activityEvent = activityEvent with
                {
                    Kind = CodexActivityEventKind.OperationStarted,
                    OperationKind = ClassifyOperationKind(
                        toolName,
                        null,
                        targetPaths,
                        TryGetInvocationArgumentsText(payload)),
                    IsMcpOperation = true,
                    McpServerName = mcpServerName,
                    TargetPaths = targetPaths,
                    Reason = string.IsNullOrWhiteSpace(toolName)
                        ? "MCP operation started"
                        : $"MCP {toolName} operation started"
                };
                return true;
            }

            default:
                return false;
        }
    }

    private static CodexActivityEventKind ResolveTerminalEventKind(JsonElement payload)
    {
        var status = TryGetFirstString(payload, "status", "outcome", "result")?.ToLowerInvariant();
        return status switch
        {
            "failed" or "error" => CodexActivityEventKind.TurnFailed,
            "interrupted" or "aborted" or "cancelled" or "canceled" => CodexActivityEventKind.TurnInterrupted,
            _ => CodexActivityEventKind.TurnCompleted
        };
    }

    private static bool TryGetAgentLifecycleEvent(
        JsonElement payload,
        out CodexActivityEventKind eventKind,
        out IReadOnlyList<string> agentThreadIds)
    {
        eventKind = CodexActivityEventKind.ContextUpdated;
        agentThreadIds = Array.Empty<string>();

        if (!payload.TryGetProperty("item", out var item) ||
            !TryGetString(item, "type", out var itemType) ||
            !string.Equals(itemType, "CollabAgentToolCall", StringComparison.OrdinalIgnoreCase) ||
            !TryGetString(item, "tool", out var toolName))
        {
            return false;
        }

        eventKind = toolName.Trim().ToLowerInvariant() switch
        {
            "spawn_agent" => CodexActivityEventKind.AgentStarted,
            "close_agent" => CodexActivityEventKind.AgentCompleted,
            _ => CodexActivityEventKind.ContextUpdated
        };
        if (eventKind == CodexActivityEventKind.ContextUpdated)
        {
            return false;
        }

        agentThreadIds = ExtractAgentThreadIds(item);
        return agentThreadIds.Count > 0;
    }

    private static IReadOnlyList<string> ExtractAgentThreadIds(JsonElement item)
    {
        var threadIds = new HashSet<string>(StringComparer.Ordinal);
        if (item.TryGetProperty("receiver_agents", out var receiverAgents))
        {
            CollectAgentThreadIds(receiverAgents, threadIds);
        }

        return threadIds.OrderBy(threadId => threadId, StringComparer.Ordinal).ToArray();
    }

    private static void CollectAgentThreadIds(JsonElement element, ISet<string> threadIds)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectAgentThreadIds(item, threadIds);
                }

                break;

            case JsonValueKind.Object:
                if (TryGetFirstString(element, "thread_id", "threadId") is { } threadId)
                {
                    AddAgentThreadId(threadIds, threadId);
                }

                break;
        }
    }

    private static void AddAgentThreadId(ISet<string> threadIds, string? threadId)
    {
        if (!string.IsNullOrWhiteSpace(threadId))
        {
            threadIds.Add(threadId.Trim());
        }
    }

    private static bool IsReasoningItem(JsonElement payload)
    {
        return payload.TryGetProperty("item", out var item) &&
            TryGetString(item, "type", out var itemType) &&
            string.Equals(itemType, "Reasoning", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetThinkingSummary(JsonElement payload, string? payloadType)
    {
        var normalizedPayloadType = payloadType?.Trim().ToLowerInvariant();
        if (normalizedPayloadType is "reasoning" or "agent_reasoning" or "agent_message")
        {
            return TryGetLastSummaryText(payload, "summary", "summary_text");
        }

        if (normalizedPayloadType == "item_completed" &&
            payload.TryGetProperty("item", out var item))
        {
            return TryGetLastSummaryText(item, "summary_text", "summary");
        }

        return null;
    }

    private static string? TryGetLastSummaryText(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out var property))
            {
                continue;
            }

            var summaryText = TryGetLastSummaryText(property);
            if (!string.IsNullOrWhiteSpace(summaryText))
            {
                var normalizedSummary = ThinkingSummaryFormatter.Normalize(summaryText);
                if (!string.IsNullOrWhiteSpace(normalizedSummary))
                {
                    return normalizedSummary;
                }
            }
        }

        return null;
    }

    private static string? TryGetLastSummaryText(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                return element.GetString();

            case JsonValueKind.Array:
            {
                string? lastSummary = null;
                foreach (var item in element.EnumerateArray())
                {
                    var candidateText = TryGetLastSummaryText(item);
                    if (!string.IsNullOrWhiteSpace(candidateText))
                    {
                        lastSummary = candidateText;
                    }
                }

                return lastSummary;
            }

            case JsonValueKind.Object:
                if (TryGetString(element, "text", out var text) && !string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                if (element.TryGetProperty("summary_text", out var summaryText))
                {
                    return TryGetLastSummaryText(summaryText);
                }

                if (element.TryGetProperty("summary", out var nestedSummary))
                {
                    return TryGetLastSummaryText(nestedSummary);
                }

                return null;

            default:
                return null;
        }
    }

    private static CodexOperationKind ClassifyOperationKind(
        string? toolName,
        string? commandText,
        IReadOnlyList<string> targetPaths,
        string? toolInput = null)
    {
        var normalizedName = toolName?.ToLowerInvariant() ?? "";
        var hasFileTarget = IsFileMutationTool(toolName) || targetPaths.Count > 0;
        if (hasFileTarget &&
            (normalizedName.Contains("create", StringComparison.Ordinal) ||
             normalizedName.Contains("add", StringComparison.Ordinal)))
        {
            return CodexOperationKind.Create;
        }

        if (hasFileTarget &&
            (normalizedName.Contains("delete", StringComparison.Ordinal) ||
             normalizedName.Contains("remove", StringComparison.Ordinal)))
        {
            return CodexOperationKind.Delete;
        }

        var patchOperationKind = TryGetPatchOperationKind(toolInput);
        if (patchOperationKind.HasValue)
        {
            return patchOperationKind.Value;
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.create_file(", "tools.createFile("))
        {
            return CodexOperationKind.Create;
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.delete_file(", "tools.deleteFile("))
        {
            return CodexOperationKind.Delete;
        }

        if (hasFileTarget)
        {
            return CodexOperationKind.Edit;
        }

        if (IsResearchToolCall(toolName, toolInput))
        {
            return CodexOperationKind.Research;
        }

        if (!string.IsNullOrWhiteSpace(commandText))
        {
            return CodexOperationKind.Command;
        }

        if (toolName is not null && IsMcpToolName(toolName))
        {
            return ClassifyMcpOperationKind(toolName);
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.apply_patch("))
        {
            return CodexOperationKind.Edit;
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.delete_file(", "tools.deleteFile("))
        {
            return CodexOperationKind.Delete;
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.exec_command(", "tools.write_stdin("))
        {
            return CodexOperationKind.Command;
        }

        if (LooksLikeNestedToolCall(toolInput, "tools.view_image(", "tools.read_mcp_resource(", "tools.script_read("))
        {
            return CodexOperationKind.Read;
        }

        var normalizedToolName = toolName?.ToLowerInvariant() ?? "";
        if (normalizedToolName == "shell_command")
        {
            return CodexOperationKind.Command;
        }

        if (normalizedToolName.Contains("read", StringComparison.Ordinal) ||
            normalizedToolName.Contains("search", StringComparison.Ordinal) ||
            normalizedToolName.Contains("view", StringComparison.Ordinal))
        {
            return CodexOperationKind.Read;
        }

        return CodexOperationKind.Unknown;
    }

    private static CodexOperationKind ClassifyMcpOperationKind(string toolName)
    {
        var normalizedToolName = (McpServerNameFormatter.ExtractToolName(toolName) ?? toolName).ToLowerInvariant();
        if (IsResearchToolName(normalizedToolName))
        {
            return CodexOperationKind.Research;
        }

        if (normalizedToolName.Contains("exec", StringComparison.Ordinal) ||
            normalizedToolName.Contains("command", StringComparison.Ordinal) ||
            normalizedToolName.Contains("evaluate", StringComparison.Ordinal) ||
            normalizedToolName.StartsWith("run", StringComparison.Ordinal))
        {
            return CodexOperationKind.Command;
        }

        return CodexOperationKind.Read;
    }

    private static bool IsResearchToolCall(string? toolName, string? toolInput)
    {
        return IsResearchToolName(toolName) ||
            LooksLikeNestedToolCall(
                toolInput,
                "tools.web__run(",
                "tools.web.run(",
                "tools.web_search(",
                "tools.search_query(",
                "web__run(",
                "web.run(",
                "web_search(");
    }

    private static bool IsResearchToolName(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        var normalizedToolName = (McpServerNameFormatter.ExtractToolName(toolName) ?? toolName)
            .Trim()
            .ToLowerInvariant();
        return normalizedToolName is
            "web_search" or
            "search" or
            "search_query" or
            "web__run" or
            "web.run" or
            "http_get" or
            "http_fetch" or
            "fetch_url" ||
            normalizedToolName.Contains("web_search", StringComparison.Ordinal) ||
            normalizedToolName.EndsWith("__search", StringComparison.Ordinal) ||
            normalizedToolName.EndsWith("__search_query", StringComparison.Ordinal);
    }

    private static string? TryGetInvocationArgumentsText(JsonElement payload)
    {
        if (!payload.TryGetProperty("invocation", out var invocation) ||
            !invocation.TryGetProperty("arguments", out var arguments))
        {
            return null;
        }

        return arguments.ValueKind == JsonValueKind.String
            ? arguments.GetString()
            : arguments.GetRawText();
    }

    private static bool LooksLikeNestedToolCall(string? input, params string[] signatures)
    {
        return !string.IsNullOrWhiteSpace(input) &&
            signatures.Any(signature => input.Contains(signature, StringComparison.OrdinalIgnoreCase));
    }

    private static string? ResolveEffectiveToolName(string? rawToolName, string? toolInput)
    {
        return ExtractNestedMcpToolName(toolInput) ?? rawToolName;
    }

    private static string? ExtractNestedMcpToolName(string? toolInput)
    {
        if (string.IsNullOrWhiteSpace(toolInput))
        {
            return null;
        }

        var searchStart = 0;
        while (searchStart < toolInput.Length)
        {
            var markerIndex = toolInput.IndexOf("mcp__", searchStart, StringComparison.OrdinalIgnoreCase);
            if (markerIndex < 0)
            {
                return null;
            }

            if (markerIndex > 0 && IsToolNameBoundaryCharacter(toolInput[markerIndex - 1]))
            {
                searchStart = markerIndex + "mcp__".Length;
                continue;
            }

            var endIndex = markerIndex + "mcp__".Length;
            while (endIndex < toolInput.Length && IsToolNameCharacter(toolInput[endIndex]))
            {
                endIndex++;
            }

            var candidate = toolInput[markerIndex..endIndex];
            if (McpServerNameFormatter.ExtractServerName(candidate) is not null)
            {
                return candidate;
            }

            searchStart = endIndex;
        }

        return null;
    }

    private static bool IsToolNameCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '-' or '.';
    }

    private static bool IsToolNameBoundaryCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character is '_' or '-';
    }

    private static bool IsInputTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return toolName.Equals("request_user_input", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("request_permission", StringComparison.OrdinalIgnoreCase) ||
            toolName.Equals("permission_request", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetInvocationToolName(JsonElement payload)
    {
        return payload.TryGetProperty("invocation", out var invocation)
            ? TryGetString(invocation, "tool")
            : null;
    }

    private static string? TryGetInvocationMcpServerName(JsonElement payload)
    {
        if (!payload.TryGetProperty("invocation", out var invocation))
        {
            return null;
        }

        return TryGetFirstString(invocation, "server", "server_name", "serverName", "mcp_server", "mcpServer") ??
            McpServerNameFormatter.ExtractServerName(TryGetString(invocation, "tool"));
    }

    private static bool IsMcpToolName(string? toolName)
    {
        return !string.IsNullOrWhiteSpace(toolName) &&
            toolName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetFirstString(JsonElement element, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (TryGetString(element, propertyName, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? TryGetDirectToolFilePath(
        JsonElement payload,
        string? payloadType,
        string? projectPath)
    {
        return TryGetDirectToolFilePaths(payload, payloadType, projectPath).LastOrDefault();
    }

    private static IReadOnlyList<string> TryGetDirectToolFilePaths(
        JsonElement payload,
        string? payloadType,
        string? projectPath)
    {
        switch (payloadType)
        {
            case "custom_tool_call":
            {
                var input = TryGetString(payload, "input");
                var toolName = ResolveEffectiveToolName(TryGetString(payload, "name"), input);
                if (input is not null)
                {
                    var patchPaths = ExtractPatchFilePaths(input)
                        .Select(path => ResolveToolFilePath(path, projectPath))
                        .Where(path => path is not null)
                        .Cast<string>()
                        .ToArray();
                    if (patchPaths.Length > 0)
                    {
                        return patchPaths;
                    }
                }

                if (TryGetShellCommandText(payload, out var shellCommand))
                {
                    return TryGetShellMutation(shellCommand, projectPath)?.TargetPaths ?? Array.Empty<string>();
                }

                return IsFileMutationTool(toolName)
                    ? TryGetMutationTargetPathsFromText(input, projectPath)
                    : Array.Empty<string>();
            }
            case "function_call":
            {
                var arguments = TryGetString(payload, "arguments");
                var functionName = ResolveEffectiveToolName(TryGetString(payload, "name"), arguments);
                var patchPaths = ExtractPatchFilePaths(arguments)
                    .Select(path => ResolveToolFilePath(path, projectPath))
                    .Where(path => path is not null)
                    .Cast<string>()
                    .ToArray();
                if (patchPaths.Length > 0)
                {
                    return patchPaths;
                }

                if (TryGetShellCommandText(payload, out var shellCommand))
                {
                    return TryGetShellMutation(shellCommand, projectPath)?.TargetPaths ?? Array.Empty<string>();
                }

                return IsFileMutationTool(functionName)
                    ? TryGetMutationTargetPathsFromText(arguments, projectPath)
                    : Array.Empty<string>();
            }
            case "mcp_tool_call":
            case "mcp_tool_call_end":
            {
                if (!payload.TryGetProperty("invocation", out var invocation) ||
                    !TryGetString(invocation, "tool", out var toolName) ||
                    !IsFileMutationTool(toolName) ||
                    !invocation.TryGetProperty("arguments", out var arguments))
                {
                    return Array.Empty<string>();
                }

                return TryGetMutationTargetPathsFromElement(arguments, projectPath);
            }
            default:
                return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> TryGetMutationTargetPathsFromText(string? value, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var patchPaths = ExtractPatchFilePaths(value)
            .Select(path => ResolveToolFilePath(path, projectPath))
            .Where(path => path is not null)
            .Cast<string>()
            .ToArray();
        if (patchPaths.Length > 0)
        {
            return patchPaths;
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return TryGetMutationTargetPathsFromElement(document.RootElement, projectPath);
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IReadOnlyList<string> TryGetMutationTargetPathsFromElement(JsonElement element, string? projectPath)
    {
        var paths = new List<string>();
        CollectMutationTargetPaths(element, projectPath, paths);
        return paths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void CollectMutationTargetPaths(JsonElement element, string? projectPath, ICollection<string> paths)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsFilePathProperty(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var path = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            var resolved = ResolveToolFilePath(path, projectPath);
                            if (!string.IsNullOrWhiteSpace(resolved))
                            {
                                paths.Add(resolved);
                            }
                        }
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        CollectMutationTargetPaths(property.Value, projectPath, paths);
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        foreach (var patchPath in ExtractPatchFilePaths(property.Value.GetString()))
                        {
                            var resolved = ResolveToolFilePath(patchPath, projectPath);
                            if (!string.IsNullOrWhiteSpace(resolved))
                            {
                                paths.Add(resolved);
                            }
                        }
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    CollectMutationTargetPaths(item, projectPath, paths);
                }

                break;

            case JsonValueKind.String:
                foreach (var patchPath in ExtractPatchFilePaths(element.GetString()))
                {
                    var resolved = ResolveToolFilePath(patchPath, projectPath);
                    if (!string.IsNullOrWhiteSpace(resolved))
                    {
                        paths.Add(resolved);
                    }
                }

                break;
        }
    }

    private static string? TryGetMutationTargetFromText(string? value, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var patchPath = ExtractLastPatchFilePath(value);
        if (patchPath is not null)
        {
            return ResolveToolFilePath(patchPath, projectPath);
        }

        try
        {
            using var document = JsonDocument.Parse(value);
            return TryGetMutationTargetFromElement(document.RootElement, projectPath);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetMutationTargetFromElement(JsonElement element, string? projectPath)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (IsFilePathProperty(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var path = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            return ResolveToolFilePath(path, projectPath);
                        }
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                    {
                        var nestedPath = TryGetMutationTargetFromElement(property.Value, projectPath);
                        if (nestedPath is not null)
                        {
                            return nestedPath;
                        }
                    }
                    else if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        var patchPath = ExtractLastPatchFilePath(property.Value.GetString());
                        if (patchPath is not null)
                        {
                            return ResolveToolFilePath(patchPath, projectPath);
                        }
                    }
                }

                return null;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var path = TryGetMutationTargetFromElement(item, projectPath);
                    if (path is not null)
                    {
                        return path;
                    }
                }

                return null;
            case JsonValueKind.String:
                var stringPatchPath = ExtractLastPatchFilePath(element.GetString());
                return stringPatchPath is null ? null : ResolveToolFilePath(stringPatchPath, projectPath);
            default:
                return null;
        }
    }

    private static string? ExtractLastPatchFilePath(string? text)
    {
        return ExtractPatchFilePaths(text).LastOrDefault();
    }

    private static IReadOnlyList<string> ExtractPatchFilePaths(string? text)
    {
        return ExtractPatchFiles(text)
            .Select(file => file.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static CodexOperationKind? TryGetPatchOperationKind(string? text)
    {
        var files = ExtractPatchFiles(text);
        if (files.Count == 0)
        {
            return null;
        }

        if (files.All(file => file.OperationKind == CodexOperationKind.Create))
        {
            return CodexOperationKind.Create;
        }

        if (files.All(file => file.OperationKind == CodexOperationKind.Delete))
        {
            return CodexOperationKind.Delete;
        }

        return CodexOperationKind.Edit;
    }

    private static IReadOnlyList<PatchFile> ExtractPatchFiles(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<PatchFile>();
        }

        var files = new List<PatchFile>();
        var normalizedText = NormalizePatchText(text);
        foreach (var line in normalizedText.Split('\n'))
        {
            var patchLine = line.TrimStart();
            foreach (var marker in PatchFileMarkers)
            {
                if (!patchLine.StartsWith(marker, StringComparison.Ordinal))
                {
                    continue;
                }

                var candidate = patchLine[marker.Length..].Trim().Trim('"', '\'', '`');
                if (IsPlausiblePatchPath(candidate))
                {
                    files.Add(new PatchFile(candidate, GetPatchOperationKind(marker)));
                }

                break;
            }
        }

        return files
            .GroupBy(file => file.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
    }

    private static string NormalizePatchText(string text)
    {
        var normalizedText = text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal);

        foreach (var linePrefix in new[] { "***", "@@", "+", "-", " " })
        {
            normalizedText = normalizedText
                .Replace($"\\r\\n{linePrefix}", $"\n{linePrefix}", StringComparison.Ordinal)
                .Replace($"\\n{linePrefix}", $"\n{linePrefix}", StringComparison.Ordinal);
        }

        return normalizedText;
    }

    private static CodexOperationKind GetPatchOperationKind(string marker)
    {
        return marker switch
        {
            "*** Add File:" => CodexOperationKind.Create,
            "*** Delete File:" => CodexOperationKind.Delete,
            _ => CodexOperationKind.Edit
        };
    }

    private static bool IsPlausiblePatchPath(string candidate)
    {
        return candidate.Length > 0 &&
            !candidate.Contains('{') &&
            !candidate.Contains('}') &&
            !candidate.Contains('\n') &&
            !candidate.Contains('\r') &&
            !candidate.StartsWith("***", StringComparison.Ordinal) &&
            !candidate.EndsWith(':');
    }

    private static string? ResolveToolFilePath(string path, string? projectPath)
    {
        var candidate = path.Trim().Trim('"', '\'', '`');
        if (candidate.Length == 0)
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Path.IsPathRooted(candidate) || string.IsNullOrWhiteSpace(projectPath)
                ? candidate
                : Path.Combine(projectPath, candidate));
        }
        catch
        {
            return candidate;
        }
    }

    private static bool IsFileMutationTool(string? toolName)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return false;
        }

        return FileMutationToolNames.Contains(toolName, StringComparer.OrdinalIgnoreCase) ||
            FileMutationToolNames.Contains(
                McpServerNameFormatter.ExtractToolName(toolName) ?? "",
                StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsFilePathProperty(string propertyName)
    {
        return FilePathPropertyNames.Contains(propertyName, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] PatchFileMarkers =
    [
        "*** Update File:",
        "*** Add File:",
        "*** Delete File:",
        "*** Move to:"
    ];

    private static readonly string[] FileMutationToolNames =
    [
        "apply_patch",
        "applyPatch",
        "write_file",
        "writeFile",
        "edit_file",
        "editFile",
        "create_file",
        "createFile",
        "delete_file",
        "deleteFile",
        "move_file",
        "moveFile",
        "replace_file",
        "replaceFile"
    ];

    private static readonly string[] ShellEditMutationCommandNames =
    [
        "Set-Content",
        "Add-Content",
        "Clear-Content",
        "Out-File",
        "Tee-Object"
    ];

    private static readonly string[] FilePathPropertyNames =
    [
        "target_file",
        "targetFile",
        "file_path",
        "filePath",
        "filename",
        "fileName",
        "path",
        "file"
    ];

    private static string? ExtractCommandName(string commandText)
    {
        foreach (var segment in SplitCommandSegments(commandText))
        {
            var token = ExtractFirstCommandToken(segment);
            if (token is null)
            {
                continue;
            }

            if (IsAssignmentPrefix(token, segment))
            {
                continue;
            }

            return SanitizeCommandName(token);
        }

        return null;
    }

    private static string? ExtractFirstCommandToken(string commandText)
    {
        var normalized = commandText.Trim();
        if (normalized.Length == 0)
        {
            return null;
        }

        while (normalized.StartsWith('&'))
        {
            normalized = normalized[1..].TrimStart();
        }

        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized[0] is '"' or '\'')
        {
            var quote = normalized[0];
            var endQuote = normalized.IndexOf(quote, 1);
            if (endQuote < 0)
            {
                return normalized[1..].Trim();
            }

            return normalized[1..endQuote].Trim();
        }

        var separatorIndex = normalized.IndexOfAny([' ', '\t', '|', ';']);
        var commandName = separatorIndex < 0 ? normalized : normalized[..separatorIndex];
        commandName = commandName.Trim();
        return commandName.Length == 0 ? null : commandName;
    }

    private static IEnumerable<string> SplitCommandSegments(string commandText)
    {
        return commandText
            .Split([';', '|', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(segment => segment.Trim())
            .Where(segment => segment.Length > 0);
    }

    private static bool IsAssignmentPrefix(string token, string segment)
    {
        if (!token.StartsWith("$", StringComparison.Ordinal))
        {
            return false;
        }

        return segment.Contains('=');
    }

    private static string? SanitizeCommandName(string commandName)
    {
        var normalized = commandName.Trim().Trim('"', '\'');
        if (normalized.Length == 0)
        {
            return null;
        }

        if (LooksLikePath(normalized))
        {
            var fileName = Path.GetFileNameWithoutExtension(normalized);
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                return fileName;
            }
        }

        return normalized;
    }

    private static bool LooksLikePath(string commandName)
    {
        return commandName.Contains('\\') ||
            commandName.Contains('/') ||
            (commandName.Length >= 2 && commandName[1] == ':');
    }

    private static string DescribeRunningCommandKind(RunningCommandKind? commandKind)
    {
        return commandKind switch
        {
            RunningCommandKind.Git => "git",
            RunningCommandKind.Search => "search",
            RunningCommandKind.Build => "build",
            RunningCommandKind.Test => "test",
            _ => "running command"
        };
    }

    private static RunningCommandKind? ClassifyShellCommand(string commandText)
    {
        var normalized = NormalizeCommandTextForClassification(commandText);
        if (normalized.Length == 0)
        {
            return null;
        }

        var commandName = ExtractCommandName(commandText);
        if (string.Equals(commandName, "git", StringComparison.OrdinalIgnoreCase))
        {
            return RunningCommandKind.Git;
        }

        if (ContainsAny(normalized, GitShellCommandMarkers))
        {
            return RunningCommandKind.Git;
        }

        if (ContainsAny(normalized, SearchingContextShellCommandMarkers))
        {
            return RunningCommandKind.Search;
        }

        if (ContainsAny(normalized, BuildingShellCommandMarkers))
        {
            return RunningCommandKind.Build;
        }

        if (ContainsAny(normalized, TestingShellCommandMarkers))
        {
            return RunningCommandKind.Test;
        }

        return null;
    }

    private static bool TryGetShellCommandText(JsonElement payload, out string commandText)
    {
        commandText = "";

        if (TryGetString(payload, "command", out commandText))
        {
            return true;
        }

        if (TryGetString(payload, "arguments", out var arguments))
        {
            try
            {
                using var document = JsonDocument.Parse(arguments);
                var root = document.RootElement;
                if (TryGetString(root, "command", out commandText))
                {
                    return true;
                }

                if (root.TryGetProperty("input", out var input) && TryGetString(input, "command", out commandText))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        if (TryGetString(payload, "input", out var toolInput))
        {
            if (string.Equals(TryGetString(payload, "name"), "shell_command", StringComparison.OrdinalIgnoreCase))
            {
                commandText = toolInput;
                return true;
            }

            if (ExtractNestedMcpToolName(toolInput) is null &&
                TryGetNestedShellCommandText(toolInput, out commandText))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetNestedShellCommandText(string toolInput, out string commandText)
    {
        commandText = "";
        var match = NestedShellCommandRegex.Match(toolInput);
        if (!match.Success)
        {
            return false;
        }

        var rawValue = match.Groups["value"].Value;
        try
        {
            commandText = JsonSerializer.Deserialize<string>($"\"{rawValue}\"") ?? rawValue;
        }
        catch
        {
            commandText = rawValue.Replace("\\\"", "\"", StringComparison.Ordinal);
        }

        return !string.IsNullOrWhiteSpace(commandText);
    }

    private static ShellMutation? TryGetShellMutation(string? commandText, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(commandText))
        {
            return null;
        }

        var targetPaths = new List<string>();
        CodexOperationKind? operationKind = null;

        foreach (var segment in SplitCommandSegments(commandText))
        {
            var tokens = TokenizeShellSegment(segment);
            for (var index = 0; index < tokens.Count; index++)
            {
                var commandName = SanitizeCommandName(tokens[index]);
                if (commandName is null)
                {
                    continue;
                }

                var mutationKind = GetShellMutationKind(commandName, tokens, index);
                if (!mutationKind.HasValue)
                {
                    continue;
                }

                var pathToken = GetShellMutationPathToken(tokens, index, commandName);
                if (!IsPlausibleShellFilePath(pathToken))
                {
                    continue;
                }

                var resolvedPath = ResolveToolFilePath(pathToken!, projectPath);
                if (string.IsNullOrWhiteSpace(resolvedPath))
                {
                    continue;
                }

                targetPaths.Add(resolvedPath);
                operationKind = operationKind is null || operationKind == mutationKind
                    ? mutationKind
                    : CodexOperationKind.Edit;
            }
        }

        if (targetPaths.Count == 0)
        {
            var commandKind = ClassifyShellCommand(commandText);
            if (commandKind is not (RunningCommandKind.Git or RunningCommandKind.Build or RunningCommandKind.Test))
            {
                foreach (var segment in SplitCommandSegments(commandText))
                {
                    var tokens = TokenizeShellSegment(segment);
                    for (var index = 0; index < tokens.Count - 1; index++)
                    {
                        var pathToken = tokens[index] switch
                        {
                            ">" or ">>" => tokens[index + 1],
                            _ when tokens[index].StartsWith(">", StringComparison.Ordinal) && tokens[index].Length > 1 => tokens[index][1..],
                            _ => null
                        };

                        if (!IsPlausibleShellFilePath(pathToken))
                        {
                            continue;
                        }

                        var resolvedPath = ResolveToolFilePath(pathToken!, projectPath);
                        if (!string.IsNullOrWhiteSpace(resolvedPath))
                        {
                            targetPaths.Add(resolvedPath);
                        }
                    }
                }

                if (targetPaths.Count > 0)
                {
                    operationKind = CodexOperationKind.Edit;
                }
            }
        }

        var distinctPaths = targetPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return distinctPaths.Length == 0 || !operationKind.HasValue
            ? null
            : new ShellMutation(operationKind.Value, distinctPaths);
    }

    private static CodexOperationKind? GetShellMutationKind(
        string commandName,
        IReadOnlyList<string> tokens,
        int commandIndex)
    {
        if (ShellEditMutationCommandNames.Contains(commandName, StringComparer.OrdinalIgnoreCase))
        {
            return CodexOperationKind.Edit;
        }

        if (string.Equals(commandName, "New-Item", StringComparison.OrdinalIgnoreCase))
        {
            var itemType = GetShellOptionValue(tokens, commandIndex + 1, "-ItemType", "-Type");
            return string.Equals(itemType, "File", StringComparison.OrdinalIgnoreCase)
                ? CodexOperationKind.Create
                : null;
        }

        if (string.Equals(commandName, "Remove-Item", StringComparison.OrdinalIgnoreCase))
        {
            return CodexOperationKind.Delete;
        }

        return null;
    }

    private static string? GetShellMutationPathToken(
        IReadOnlyList<string> tokens,
        int commandIndex,
        string commandName)
    {
        var optionNames = string.Equals(commandName, "Tee-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "Out-File", StringComparison.OrdinalIgnoreCase)
            ? new[] { "-FilePath", "-LiteralPath" }
            : new[] { "-LiteralPath", "-Path" };
        var optionPath = GetShellOptionValue(tokens, commandIndex + 1, optionNames);
        if (IsPlausibleShellFilePath(optionPath))
        {
            return optionPath;
        }

        if (commandIndex + 1 < tokens.Count &&
            IsPlausibleShellFilePath(tokens[commandIndex + 1]) &&
            !tokens[commandIndex + 1].StartsWith("-", StringComparison.Ordinal))
        {
            return tokens[commandIndex + 1];
        }

        return null;
    }

    private static string? GetShellOptionValue(
        IReadOnlyList<string> tokens,
        int startIndex,
        params string[] optionNames)
    {
        for (var index = startIndex; index < tokens.Count; index++)
        {
            var token = tokens[index];
            foreach (var optionName in optionNames)
            {
                if (token.StartsWith(optionName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return token[(optionName.Length + 1)..];
                }

                if (string.Equals(token, optionName, StringComparison.OrdinalIgnoreCase) &&
                    index + 1 < tokens.Count)
                {
                    return tokens[index + 1];
                }
            }
        }

        return null;
    }

    private static bool IsPlausibleShellFilePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var candidate = value.Trim().Trim('"', '\'', '`');
        return candidate.Length > 0 &&
            !candidate.StartsWith("-", StringComparison.Ordinal) &&
            !candidate.StartsWith("$", StringComparison.Ordinal) &&
            candidate is not "." and not ".." &&
            !candidate.Contains('\n') &&
            !candidate.Contains('\r') &&
            !candidate.Contains("$null", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> TokenizeShellSegment(string segment)
    {
        var tokens = new List<string>();
        var token = new System.Text.StringBuilder();
        char quote = '\0';

        void FlushToken()
        {
            if (token.Length > 0)
            {
                tokens.Add(token.ToString());
                token.Clear();
            }
        }

        foreach (var character in segment)
        {
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }
                else
                {
                    token.Append(character);
                }

                continue;
            }

            if (character is '"' or '\'')
            {
                quote = character;
            }
            else if (char.IsWhiteSpace(character))
            {
                FlushToken();
            }
            else
            {
                token.Append(character);
            }
        }

        FlushToken();
        return tokens;
    }

    private static bool IsPassiveShellCommand(string commandText)
    {
        var commandName = ExtractCommandName(commandText);
        return string.Equals(commandName, "Start-Sleep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "Sleep", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(commandName, "timeout", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeCommandTextForClassification(string commandText)
    {
        var split = SplitCommandText(commandText);
        if (split is null)
        {
            return commandText.Trim();
        }

        if (string.IsNullOrWhiteSpace(split.Value.Remainder))
        {
            return split.Value.CommandName;
        }

        return $"{split.Value.CommandName} {split.Value.Remainder}";
    }

    private static CommandTextParts? SplitCommandText(string commandText)
    {
        var normalized = commandText.TrimStart();
        if (normalized.Length == 0)
        {
            return null;
        }

        while (normalized.StartsWith('&'))
        {
            normalized = normalized[1..].TrimStart();
        }

        if (normalized.Length == 0)
        {
            return null;
        }

        string token;
        string remainder;

        if (normalized[0] is '"' or '\'')
        {
            var quote = normalized[0];
            var endQuote = normalized.IndexOf(quote, 1);
            if (endQuote < 0)
            {
                token = normalized[1..];
                remainder = "";
            }
            else
            {
                token = normalized[1..endQuote];
                remainder = normalized[(endQuote + 1)..];
            }
        }
        else
        {
            var separatorIndex = normalized.IndexOfAny([' ', '\t', '|', ';']);
            if (separatorIndex < 0)
            {
                token = normalized;
                remainder = "";
            }
            else
            {
                token = normalized[..separatorIndex];
                remainder = normalized[separatorIndex..];
            }
        }

        token = token.Trim();
        if (token.Length == 0)
        {
            return null;
        }

        return new CommandTextParts(SanitizeCommandName(token) ?? token, remainder.TrimStart());
    }

    private readonly record struct CommandTextParts(string CommandName, string Remainder);

    private static bool ContainsAny(string value, IEnumerable<string> needles)
    {
        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static readonly string[] GitShellCommandMarkers =
    [
        "git status",
        "git diff",
        "git show",
        "git log",
        "git blame",
        "git stash show"
    ];

    private static readonly string[] SearchingContextShellCommandMarkers =
    [
        "Get-Content",
        "Get-ChildItem",
        "Select-String",
        "rg ",
        "rg(",
        "ripgrep",
        "cat ",
        " type ",
        "type ",
        "less ",
        "more ",
        "find ",
        "dir ",
        "ls ",
        "Get-Process",
        "grep ",
        "ack "
    ];

    private static readonly string[] BuildingShellCommandMarkers =
    [
        "dotnet build",
        "dotnet publish",
        "npm run build",
        "pnpm run build",
        "yarn build",
        "bun run build",
        "cargo build",
        "go build",
        "cmake --build",
        "mvn package",
        "gradle build",
        "webpack",
        "vite build",
        "tsc",
        "make "
    ];

    private static readonly string[] TestingShellCommandMarkers =
    [
        "dotnet test",
        "npm test",
        "npm run test",
        "pnpm test",
        "pnpm run test",
        "yarn test",
        "yarn run test",
        "bun test",
        "go test",
        "cargo test",
        "pytest",
        "mvn test",
        "gradle test",
        "make test",
        "make check",
        "jest",
        "vitest",
        "playwright test",
        "npx playwright test"
    ];

    private static string? TryGetCollaborationMode(JsonElement payload)
    {
        string? mode = null;

        if (payload.TryGetProperty("collaboration_mode", out var collaborationMode))
        {
            mode = TryGetNormalizedCollaborationMode(collaborationMode, "mode");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            mode = TryGetNormalizedCollaborationMode(collaborationMode, "kind");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            mode = TryGetNormalizedCollaborationMode(collaborationMode, "value");
            if (!string.IsNullOrWhiteSpace(mode))
            {
                return mode;
            }

            if (collaborationMode.TryGetProperty("settings", out var settings))
            {
                mode = TryGetNormalizedCollaborationMode(settings, "mode");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }

                mode = TryGetNormalizedCollaborationMode(settings, "kind");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }

                mode = TryGetNormalizedCollaborationMode(settings, "value");
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    return mode;
                }
            }
        }

        mode = TryGetNormalizedCollaborationMode(payload, "collaboration_mode_kind");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        mode = TryGetNormalizedCollaborationMode(payload, "collaboration_mode_mode");
        if (!string.IsNullOrWhiteSpace(mode))
        {
            return mode;
        }

        return null;
    }

    private static string? TryGetNormalizedCollaborationMode(JsonElement element, string propertyName)
    {
        if (!TryGetString(element, propertyName, out var value))
        {
            return null;
        }

        return NormalizeCollaborationMode(value);
    }

    private static string? NormalizeCollaborationMode(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var compact = new string(trimmed.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return compact switch
        {
            "plan" or "planmode" => "plan",
            "goal" or "goalmode" => "plan",
            _ => trimmed.ToLowerInvariant()
        };
    }

    private static DateTime? TryGetTimestamp(JsonElement root)
    {
        if (!root.TryGetProperty("timestamp", out var prop) || prop.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var timeStr = prop.GetString();
        if (DateTime.TryParse(timeStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var dt))
        {
            return dt;
        }

        return null;
    }

    private static string? TryGetString(JsonElement element, string propertyName)
    {
        return TryGetString(element, propertyName, out var value) ? value : null;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        value = "";
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = property.GetString() ?? "";
        return true;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }

    private SessionInspection? SelectInspection(
        IReadOnlyList<SessionInspectionCandidate> candidates,
        string? normalizedProjectPath)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        SessionInspectionCandidate? best = null;

        if (!string.IsNullOrWhiteSpace(normalizedProjectPath))
        {
            best = PickBest(candidates.Where(candidate => candidate.Inspection.MatchesProject && candidate.Inspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes)));
            if (best is not null)
            {
                return best.Inspection;
            }

            best = PickBest(candidates.Where(candidate => candidate.Inspection.MatchesProject));
            if (best is not null)
            {
                return best.Inspection;
            }
        }

        best = PickBest(candidates.Where(candidate => candidate.Inspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes)));
        if (best is not null)
        {
            return best.Inspection;
        }

        return PickBest(candidates)?.Inspection;
    }

    private string? SelectLatestObservedProjectPath(IReadOnlyList<SessionInspectionCandidate> candidates)
    {
        var best = PickBest(candidates.Where(candidate => candidate.Inspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes) && !string.IsNullOrWhiteSpace(candidate.Inspection.ProjectPath)))
            ?? PickBest(candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate.Inspection.ProjectPath)));

        return best?.Inspection.ProjectPath;
    }

    private SessionInspectionCandidate? PickBest(IEnumerable<SessionInspectionCandidate> candidates)
    {
        var nowUtc = DateTime.UtcNow;
        return candidates
            .OrderByDescending(candidate => HasPendingMutation(candidate.Inspection, nowUtc))
            .ThenByDescending(candidate => HasPendingOperation(candidate.Inspection, nowUtc))
            .ThenByDescending(candidate => candidate.Inspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes))
            .ThenByDescending(candidate => candidate.Inspection.LastObservedAt ?? DateTime.MinValue)
            .ThenByDescending(candidate => candidate.SessionLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool HasPendingMutation(SessionInspection inspection, DateTime nowUtc)
    {
        return inspection.GetActivityStateAt(nowUtc)?.PendingMutationCount > 0;
    }

    private static bool HasPendingOperation(SessionInspection inspection, DateTime nowUtc)
    {
        return inspection.GetActivityStateAt(nowUtc)?.PendingOperationCount > 0;
    }

    private sealed record PatchFile(string Path, CodexOperationKind OperationKind);

    private readonly record struct ShellMutation(
        CodexOperationKind OperationKind,
        IReadOnlyList<string> TargetPaths);

    private sealed record CachedSessionInspection(
        long Length,
        DateTime LastWriteTimeUtc,
        SessionInspection Inspection);

    private sealed record SessionInspectionCandidate(SessionInspection Inspection, DateTime SessionLastWriteTimeUtc);
}
