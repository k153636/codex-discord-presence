namespace CodexDiscordPresence;

internal enum CodexActivityEventKind
{
    TurnStarted = 0,
    Reasoning = 1,
    OperationStarted = 2,
    OperationCompleted = 3,
    InputRequested = 4,
    InputResolved = 5,
    TurnCompleted = 6,
    TurnFailed = 7,
    TurnInterrupted = 8,
    ContextUpdated = 9
}

internal enum CodexOperationKind
{
    Unknown = 0,
    Read = 1,
    Edit = 2,
    Create = 3,
    Delete = 4,
    Command = 5
}

internal enum CodexTurnLifecycle
{
    None = 0,
    Open = 1,
    WaitingForInput = 2,
    Completed = 3,
    Failed = 4,
    Interrupted = 5,
    Stalled = 6
}

internal enum CodexActivitySource
{
    SessionLog = 0,
    AppServer = 1,
    Hook = 2,
    FileSystem = 3,
    Git = 4
}

internal sealed record CodexActivityEvent
{
    public long Sequence { get; init; }
    public DateTime TimestampUtc { get; init; }
    public CodexActivityEventKind Kind { get; init; }
    public string? TurnId { get; init; }
    public string? CallId { get; init; }
    public CodexOperationKind OperationKind { get; init; }
    public bool IsMcpOperation { get; init; }
    public string? McpServerName { get; init; }
    public string? ThinkingSummary { get; init; }
    public IReadOnlyList<string> TargetPaths { get; init; } = Array.Empty<string>();
    public RunningCommandKind CommandKind { get; init; } = RunningCommandKind.Unknown;
    public string? CommandName { get; init; }
    public string? Reason { get; init; }
    public CodexActivitySource Source { get; init; } = CodexActivitySource.SessionLog;

    public bool IsEffective => Kind is
        CodexActivityEventKind.TurnStarted or
        CodexActivityEventKind.Reasoning or
        CodexActivityEventKind.OperationStarted or
        CodexActivityEventKind.OperationCompleted or
        CodexActivityEventKind.InputRequested or
        CodexActivityEventKind.InputResolved or
        CodexActivityEventKind.TurnCompleted or
        CodexActivityEventKind.TurnFailed or
        CodexActivityEventKind.TurnInterrupted;
}
