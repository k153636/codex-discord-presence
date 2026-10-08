using System.Text.RegularExpressions;

namespace CodexDiscordPresence;

internal sealed record ClaudeCodeActivitySnapshot(
    CodexActivityKind ActivityKind,
    DateTime? ActivityStartedAt,
    DateTime? LastObservedAt,
    string? ActiveTurnId) : IPresenceActivitySnapshot
{
    public bool IsRunning => true;
    public string? ProcessName => "claude";
    public bool IsThinking => ActivityKind.IsThinking();
    public ActivityConfidence Confidence => ActivityConfidence.High;
    public ActivityProvenance ActivityProvenance => ActivityProvenance.Observed;
    public string ActivityReason { get; init; } = "Claude Code lifecycle hook";
    public string? CollaborationMode => null;
    public DateTime? LastTaskStartedAt => null;
    public RunningCommandKind RunningCommandKind => RunningCommandKind.Unknown;
    public string RunningCommandName => "";
    public string? LastDirectToolFilePath => ActiveToolFilePath;
    public DateTime? LastDirectToolFileAt => ActiveToolFilePath is null ? null : LastObservedAt;
    public string? ActiveToolFilePath => ActivityFilePaths.FirstOrDefault();
    public bool IsMcpOperation => ActiveMcpServerNames.Count > 0;
    public string? McpServerName => ActiveMcpServerNames.FirstOrDefault();
    public IReadOnlyList<string> ActiveMcpServerNames { get; init; } = [];
    public CodexActivityEventKind? LatestActivityEventKind { get; init; }
    public IReadOnlyList<string> ActivityFilePaths { get; init; } = [];
    public string? ActiveActivityDescription { get; init; }
    public int PendingOperationCount { get; init; }
    public int PendingMutationCount => ActivityFilePaths.Count;
    public DateTime? LastEffectiveSignalAt => LastObservedAt;
    public string? LatestThinkingSummary { get; init; }
    public int? PartySize { get; init; }
    public bool IsSuccessfulCompletion => false;
    public bool IsError { get; init; }
    public bool HasDirectActivityEvidence => true;
    public IReadOnlyList<RecentProjectFileSnapshot> RecentEditedFiles => [];
    public int ActivityRepeatCount => 1;
    public string? ProviderState => null;
}

internal static class ClaudeCodePresenceProjection
{
    internal static ClaudeCodeActivitySnapshot Build(ClaudeCodeSessionObservation observation, string? spinnerLabel = null)
    {
        var tool = observation.Tools.LastOrDefault();
        var waiting = observation.EventName is "PermissionRequest" or "Notification";
        var kind = waiting ? CodexActivityKind.WaitingForInput : tool is not null ? ClassifyTool(tool.Name) :
            observation.EventName is "Stop" or "SessionStart" ? CodexActivityKind.Ready : CodexActivityKind.AnalyzingProject;
        var servers = waiting ? [] : observation.Tools.Select(tool => McpServer(tool.Name))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var files = waiting || kind is not (CodexActivityKind.ApplyingEdits or CodexActivityKind.ReadingFiles)
            ? []
            : observation.Tools.Where(item => ClassifyTool(item.Name) == kind)
                .Select(item => item.FileName).OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        return new ClaudeCodeActivitySnapshot(kind, observation.ActivityStartedAtUtc.UtcDateTime,
            observation.ObservedAtUtc.UtcDateTime, observation.SessionId)
        {
            ActivityReason = observation.FromTranscript ? "Claude Code main-session transcript" : "Claude Code lifecycle hook",
            LatestActivityEventKind = tool is not null ? CodexActivityEventKind.OperationStarted :
                kind.IsThinking() ? CodexActivityEventKind.Reasoning : null,
            ActiveMcpServerNames = servers,
            ActivityFilePaths = files,
            ActiveActivityDescription = kind == CodexActivityKind.ReadingFiles && files.Length > 0
                ? "Reading " + files[0] : null,
            PendingOperationCount = waiting ? 0 : observation.Tools.Count,
            LatestThinkingSummary = kind.IsThinking() && tool is null ? ClaudeCodeHookParser.SafeText(spinnerLabel, 48) : null,
            PartySize = observation.ActiveAgentIds.Count > 0 ? observation.ActiveAgentIds.Count + 1 : null,
            IsError = observation.EventName == "PostToolUseFailure"
        };
    }

    internal static PresenceContext CreateContext(ClaudeCodeSessionObservation observation,
        ProjectSnapshot project, GitSnapshot git, SessionSnapshot session, string? spinnerLabel = null)
    {
        var metadata = ClaudeCodeTranscriptMetadata.Read(observation.TranscriptPath, observation.SessionId);
        var model = metadata.Model ?? observation.Model;
        return new PresenceContext(NormalizeModel(model), Build(observation, spinnerLabel), project, git, session,
            new TokenUsageSnapshot(null, null))
        {
            ProviderId = ProviderIds.ClaudeCode,
            FeatureLabel = observation.UsesClaudeDesign ? "Claude Design" : null,
            ModelReasoningLevel = metadata.Effort
        };
    }

    internal static string NormalizeModel(string? model)
    {
        var safe = ClaudeCodeHookParser.SafeText(model);
        return safe is null || safe.Contains('/') || safe.Contains('\\')
            ? "Claude Code"
            : Regex.Replace(safe, @"(?<=\d)-(?=\d)", ".")
                .Replace('-', ' ').Replace('_', ' ').ToLowerInvariant();
    }

    private static CodexActivityKind ClassifyTool(string name) => name switch
    {
        "Edit" or "Write" or "NotebookEdit" => CodexActivityKind.ApplyingEdits,
        "Read" or "Glob" or "Grep" => CodexActivityKind.ReadingFiles,
        "WebSearch" or "WebFetch" => CodexActivityKind.Researching,
        "Bash" or "PowerShell" => CodexActivityKind.RunningCommand,
        "EnterPlanMode" => CodexActivityKind.Planning,
        _ when McpServer(name) is not null => CodexActivityKind.RunningCommand,
        _ => CodexActivityKind.AnalyzingProject
    };

    private static string? McpServer(string toolName)
    {
        if (!toolName.StartsWith("mcp__", StringComparison.Ordinal))
        {
            return null;
        }
        var separator = toolName.IndexOf("__", 5, StringComparison.Ordinal);
        return separator <= 5 ? null : McpServerNameFormatter.Format(toolName[5..separator]);
    }
}
