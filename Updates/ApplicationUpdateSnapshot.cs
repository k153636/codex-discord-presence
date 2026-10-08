namespace CodexDiscordPresence;

internal enum ApplicationUpdateStatus
{
    Disabled, Checking, UpToDate, Available, Downloading, Ready, Restarting, Failed
}

internal sealed record ApplicationUpdateSnapshot(
    ApplicationUpdateStatus Status,
    string CurrentVersion,
    string? AvailableVersion,
    bool CanInstall,
    bool AutomaticEnabled,
    int Progress = 0,
    DateTime? RestartAtUtc = null,
    DateTime? DeferredUntilUtc = null,
    string? Error = null,
    bool BackgroundChecksEnabled = true);

internal interface IApplicationUpdateBackend
{
    bool IsInstalled { get; }
    string? PreparedVersion { get; }
    Task<string?> CheckAsync(CancellationToken cancellationToken);
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);
    void ScheduleRestart(string[] arguments);
}
