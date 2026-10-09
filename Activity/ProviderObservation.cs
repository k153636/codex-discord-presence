namespace CodexDiscordPresence;

internal interface IProviderObservationParser
{
    bool TryParse(
        ReadOnlySpan<byte> utf8Json,
        DateTimeOffset observedAtUtc,
        out ProviderObservation? observation);
}

internal enum ProviderObservationSource
{
    Unknown = 0,
    AntigravityCli = 1
}

internal enum ProviderAgentState
{
    Unknown = 0,
    Idle = 1,
    Thinking = 2,
    Working = 3,
    ToolUse = 4,
    Initializing = 5
}

internal enum ProviderExecutionMode
{
    Unknown = 0,
    Planning = 1,
    Fast = 2
}

internal sealed record ProviderOperationObservation(
    CodexOperationKind Kind,
    string? ToolName = null,
    string? Action = null,
    string? Summary = null,
    string? TargetPath = null,
    bool IsCompleted = false,
    DateTimeOffset? ObservedAtUtc = null);

internal sealed record ProviderQuotaObservation(
    string Id,
    decimal RemainingFraction,
    DateTimeOffset? ResetAtUtc,
    string? Window = null);

internal sealed record ProviderObservation(
    ProviderObservationSource Source,
    DateTimeOffset ObservedAtUtc,
    ProviderAgentState AgentState,
    ProviderModelObservation? Model,
    ProviderWorkspaceObservation? Workspace,
    string? ConversationId,
    ProviderExecutionMode ExecutionMode = ProviderExecutionMode.Unknown,
    ProviderContextWindowObservation? ContextWindow = null,
    int? ActiveSubagentCount = null,
    IReadOnlyList<ProviderQuotaObservation>? Quotas = null,
    string? PlanTier = null)
{
    internal const int MaxActiveSubagentCount = 64;

    // These fields stay inside the local activity pipeline. They are intentionally
    // not part of the public observation serialization surface because they can
    // contain local transcript paths or tool arguments.
    internal string? TranscriptPath { get; init; }
    internal string? ArtifactDirectoryPath { get; init; }
    internal ProviderOperationObservation? Operation { get; init; }
    internal bool IsWaitingForInput { get; init; }
    internal IReadOnlyList<SubagentWorkKind>? ActiveSubagentWorkKinds { get; init; }
}

internal sealed record ProviderModelObservation(
    string? Id,
    string? DisplayName);

internal sealed record ProviderContextWindowObservation(
    long? TotalInputTokens,
    long? TotalOutputTokens)
{
    public long? TotalTokens
    {
        get
        {
            if (!TotalInputTokens.HasValue || !TotalOutputTokens.HasValue ||
                TotalInputTokens.Value < 0 || TotalOutputTokens.Value < 0)
            {
                return null;
            }

            try
            {
                return checked(TotalInputTokens.Value + TotalOutputTokens.Value);
            }
            catch (OverflowException)
            {
                return null;
            }
        }
    }
}

internal sealed class ProviderWorkspaceObservation
{
    private const int MaxSafeNameLength = 128;
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly string? _currentDirectoryPath;
    private readonly string? _cwdPath;
    private readonly string? _projectDirectoryPath;

    internal ProviderWorkspaceObservation(
        string? cwd,
        string? currentDirectory,
        string? projectDirectory)
    {
        _cwdPath = NormalizePath(cwd);
        _currentDirectoryPath = NormalizePath(currentDirectory);
        _projectDirectoryPath = NormalizePath(projectDirectory);
        WorkspaceName = GetSafePathLeaf(currentDirectory ?? cwd);
        ProjectName = GetSafePathLeaf(projectDirectory);
    }

    public string? WorkspaceName { get; }
    public string? ProjectName { get; }

    public bool MatchesProjectPath(string? projectPath)
    {
        var normalizedProjectPath = NormalizePath(projectPath);
        return normalizedProjectPath is not null &&
            (_projectDirectoryPath is not null && PathComparer.Equals(_projectDirectoryPath, normalizedProjectPath) ||
             _currentDirectoryPath is not null && PathComparer.Equals(_currentDirectoryPath, normalizedProjectPath) ||
             _cwdPath is not null && PathComparer.Equals(_cwdPath, normalizedProjectPath));
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim();
        if (!Path.IsPathFullyQualified(trimmed))
        {
            return null;
        }

        try
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(trimmed));
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

    private static string? GetSafePathLeaf(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.Trim().TrimEnd('/', '\\');
        if (trimmed.Length == 0)
        {
            return null;
        }

        var separatorIndex = trimmed.LastIndexOfAny(['/', '\\']);
        var leaf = separatorIndex >= 0 ? trimmed[(separatorIndex + 1)..] : trimmed;
        return string.IsNullOrWhiteSpace(leaf) ? null : NormalizeText(leaf);
    }

    private static string? NormalizeText(string value)
    {
        var normalized = string.Join(
            ' ',
            new string(value.Where(character => !char.IsControl(character)).ToArray())
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length == 0)
        {
            return null;
        }

        if (normalized.Length <= MaxSafeNameLength)
        {
            return normalized;
        }

        var truncated = normalized[..MaxSafeNameLength];
        return char.IsHighSurrogate(truncated[^1])
            ? truncated[..^1]
            : truncated;
    }
}
