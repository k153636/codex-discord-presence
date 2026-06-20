using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class CodexSessionLogParser
{
    private readonly CodexDetectionOptions _options;
    private readonly PresenceTemplateOptions _presenceOptions;

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
                var inspection = AnalyzeSessionFile(file.FullName, normalizedProjectPath);
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
                var inspection = AnalyzeSessionFile(file.FullName, normalizedProjectPath: null);
                candidates.Add(new SessionInspectionCandidate(inspection, file.LastWriteTimeUtc));
            }

            return SelectLatestObservedProjectPath(candidates);
        }
        catch
        {
            return null;
        }
    }

    private SessionInspection AnalyzeSessionFile(string path, string? normalizedProjectPath)
    {
        var hasProjectPath = false;
        var matchesProject = false;
        string? latestProjectPath = null;
        var hasTaskStarted = false;
        var hasTaskCompleted = false;
        DateTime? lastTaskStartedAt = null;
        DateTime? lastTaskCompletedAt = null;
        DateTime? lastObservedAt = null;
        DateTime? lastShellCommandAt = null;
        RunningCommandKind lastRunningCommandKind = RunningCommandKind.Unknown;
        string? lastRunningCommandName = null;
        bool lastShellCommandWasInvestigative = false;
        string? collaborationMode = null;
        var pendingShellCommands = new HashSet<string>(StringComparer.Ordinal);
        var completedShellCommands = new HashSet<string>(StringComparer.Ordinal);
        string? runningCommandReason = null;

        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);

            string? line;
            while ((line = reader.ReadLine()) != null)
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
                    if (normalizedProjectPath != null && NormalizePath(cwd) == normalizedProjectPath)
                    {
                        matchesProject = true;
                    }
                }

                var payloadType = TryGetString(payload, "type", out var type) ? type : null;

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
                        var isInvestigative = commandKind is RunningCommandKind.Git or RunningCommandKind.Search;
                        if (!lastShellCommandAt.HasValue || timestamp >= lastShellCommandAt)
                        {
                            lastShellCommandAt = timestamp;
                            lastShellCommandWasInvestigative = isInvestigative;
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
            matchesProject,
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
            LastShellCommandWasInvestigative = lastShellCommandWasInvestigative
        };
    }

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

        if (!TryGetString(payload, "arguments", out var arguments))
        {
            return false;
        }

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

        return false;
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
        return candidates
            .OrderByDescending(candidate => candidate.Inspection.HasRecentActivity(_presenceOptions.ThinkingStaleTimeoutMinutes))
            .ThenByDescending(candidate => candidate.Inspection.LastObservedAt ?? DateTime.MinValue)
            .ThenByDescending(candidate => candidate.SessionLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private sealed record SessionInspectionCandidate(SessionInspection Inspection, DateTime SessionLastWriteTimeUtc);
}
