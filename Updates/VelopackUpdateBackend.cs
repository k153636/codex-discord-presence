using Velopack;
using Velopack.Sources;

namespace CodexDiscordPresence;

internal sealed class VelopackUpdateBackend : IApplicationUpdateBackend, IDisposable
{
    public const string RepositoryUrl = "https://github.com/k153636/codex-discord-presence";
    public const string ReleasesUrl = RepositoryUrl + "/releases/latest";
    private readonly UpdateManager _manager;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private UpdateInfo? _update;

    public VelopackUpdateBackend()
        : this(new UpdateManager(new GithubSource(RepositoryUrl, null, false),
            new UpdateOptions { ExplicitChannel = "win", AllowVersionDowngrade = false }))
    {
    }

    internal VelopackUpdateBackend(UpdateManager manager) => _manager = manager;

    public bool IsInstalled => _manager.IsInstalled;
    public string? PreparedVersion => IsInstalled ? _manager.UpdatePendingRestart?.Version.ToString() : null;

    public async Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        if (!IsInstalled)
        {
            var result = await new GitHubReleaseChecker(_http).CheckLatestReleaseAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.Succeeded)
            {
                if (result.RetryAfterUtc is { } retryAfter)
                    throw new ApplicationUpdateRetryException(result.WarningMessage, retryAfter);
                throw new InvalidOperationException(result.WarningMessage);
            }
            return result.UpdateAvailable ? result.LatestVersion?.ToString() : null;
        }

        UpdateInfo? update;
        try
        {
            update = await _manager.CheckForUpdatesAsync().WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Historical EXE-only releases have no Velopack feed. Treat them as current
            // when their normalized release number is not newer than this installation.
            var legacy = await new GitHubReleaseChecker(_http).CheckLatestReleaseAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (legacy.RetryAfterUtc is { } retryAfter)
                throw new ApplicationUpdateRetryException(legacy.WarningMessage, retryAfter);
            if (legacy.Succeeded && !legacy.UpdateAvailable) return null;
            throw;
        }
        cancellationToken.ThrowIfCancellationRequested();
        _update = update;
        return update?.TargetFullRelease.Version.ToString();
    }

    public Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken)
    {
        if (!IsInstalled || _update is null)
        {
            throw new InvalidOperationException("No installable update is available.");
        }
        // Velopack verifies package size/checksum and falls back from deltas to full packages.
        return _manager.DownloadUpdatesAsync(_update, progress, cancellationToken);
    }

    public void ScheduleRestart(string[] arguments)
    {
        if (!IsInstalled || _manager.UpdatePendingRestart is not { } prepared)
        {
            throw new InvalidOperationException("No verified update is ready to install.");
        }
        // The application saves state and disposes RPC before exiting; do not kill it here.
        _manager.WaitExitThenApplyUpdates(prepared, silent: true, restart: true, restartArgs: arguments);
    }

    public void Dispose() => _http.Dispose();
}
