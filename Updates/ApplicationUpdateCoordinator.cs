namespace CodexDiscordPresence;

internal sealed class ApplicationUpdateCoordinator
{
    private readonly IApplicationUpdateBackend _backend;
    private readonly UpdatePreferencesStore _store;
    private readonly Func<bool> _canRestart;
    private readonly Action _preserveSettings;
    private readonly Func<string[]> _restartArguments;
    private readonly Action _exit;
    private readonly Action<string> _log;
    private readonly bool _checksEnabled;
    private readonly SemanticVersion _currentVersion;
    private readonly object _sync = new();
    private UpdatePreferences _preferences;
    private ApplicationUpdateSnapshot _snapshot;
    private string? _availableVersion;
    private string? _readyVersion;
    private DateTime _nextCheckUtc;
    private DateTime _retryAfterUtc;
    private DateTime _serverRetryAfterUtc;
    private int _failedAttempts;
    private DateTime? _idleSinceUtc;
    private int _checkRequested;
    private int _restartRequested;
    private int _preferencesChanged;

    public ApplicationUpdateCoordinator(IApplicationUpdateBackend backend, UpdatePreferencesStore store,
        Func<bool> canRestart, Action preserveSettings, Func<string[]> restartArguments,
        Action exit, Action<string> log, bool checksEnabled = true, SemanticVersion? currentVersion = null)
    {
        _backend = backend;
        _store = store;
        _canRestart = canRestart;
        _preserveSettings = preserveSettings;
        _restartArguments = restartArguments;
        _exit = exit;
        _log = log;
        _checksEnabled = checksEnabled;
        _currentVersion = currentVersion ?? AppVersion.Current;
        try { _preferences = store.Load(); }
        catch (Exception ex)
        {
            _preferences = new UpdatePreferences(AutomaticEnabled: false);
            log($"Update preferences could not be read; automatic updates are disabled: {ex.Message}");
        }
        _snapshot = new ApplicationUpdateSnapshot(ApplicationUpdateStatus.Disabled,
            _currentVersion.ToString(), null, backend.IsInstalled, _preferences.AutomaticEnabled && checksEnabled,
            BackgroundChecksEnabled: checksEnabled);
        _checkRequested = checksEnabled ? 1 : 0;
        try { _readyVersion = NewerVersion(backend.PreparedVersion); }
        catch (Exception ex) { log($"A pending update could not be read and will be checked again: {ex.Message}"); }
    }

    public ApplicationUpdateSnapshot Snapshot
    {
        get
        {
            lock (_sync) return _snapshot with
            {
                AutomaticEnabled = _preferences.AutomaticEnabled && _checksEnabled,
                DeferredUntilUtc = _preferences.DeferredUntilUtc,
                RestartAtUtc = Volatile.Read(ref _preferencesChanged) == 0 ? _snapshot.RestartAtUtc : null
            };
        }
    }

    public void RequestCheck() => Interlocked.Exchange(ref _checkRequested, 1);
    public void RequestRestart() => Interlocked.Exchange(ref _restartRequested, 1);

    public void SetAutomaticEnabled(bool enabled)
    {
        lock (_sync) SavePreferences(_preferences with { AutomaticEnabled = enabled });
    }

    public void DeferUntil(DateTime untilUtc)
    {
        lock (_sync) SavePreferences(_preferences with { DeferredUntilUtc = untilUtc.ToUniversalTime() });
    }

    private void SavePreferences(UpdatePreferences preferences)
    {
        _store.Save(preferences); // Change the in-memory preference only after the atomic write succeeds.
        _preferences = preferences;
        Interlocked.Exchange(ref _preferencesChanged, 1);
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                await TickAsync(DateTime.UtcNow, cancellationToken);
                if (Snapshot.Status == ApplicationUpdateStatus.Restarting) return;
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    internal async Task TickAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Interlocked.Exchange(ref _preferencesChanged, 0) != 0) _idleSinceUtc = null;
        var manualRestart = Interlocked.Exchange(ref _restartRequested, 0) != 0;
        var manualCheck = Interlocked.Exchange(ref _checkRequested, 0) != 0;
        try
        {
            if (_readyVersion is null && nowUtc >= _serverRetryAfterUtc && (manualCheck ||
                (_checksEnabled && nowUtc >= _nextCheckUtc && nowUtc >= _retryAfterUtc)))
            {
                Publish(ApplicationUpdateStatus.Checking);
                _availableVersion = NewerVersion(await _backend.CheckAsync(cancellationToken));
                _nextCheckUtc = nowUtc.AddHours(6);
                _retryAfterUtc = DateTime.MinValue;
                _serverRetryAfterUtc = DateTime.MinValue;
                _failedAttempts = 0;
                Publish(_availableVersion is null ? ApplicationUpdateStatus.UpToDate : ApplicationUpdateStatus.Available);
            }

            var automatic = AutomaticAllowed(nowUtc);
            if (_readyVersion is null && _availableVersion is not null && _backend.IsInstalled && nowUtc >= _serverRetryAfterUtc &&
                (manualRestart || (automatic && nowUtc >= _retryAfterUtc)))
            {
                Publish(ApplicationUpdateStatus.Downloading);
                await _backend.DownloadAsync(progress => Publish(ApplicationUpdateStatus.Downloading,
                    progress: Math.Clamp(progress, 0, 100)), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _readyVersion = NewerVersion(_backend.PreparedVersion)
                    ?? throw new InvalidDataException("The downloaded update was not verified.");
                _retryAfterUtc = DateTime.MinValue;
                _failedAttempts = 0;
            }

            if (_readyVersion is null) return;
            if (manualRestart)
            {
                Restart(cancellationToken, automatic: false, nowUtc);
                return;
            }
            if (!AutomaticAllowed(nowUtc) || !_canRestart() || nowUtc < _retryAfterUtc)
            {
                _idleSinceUtc = null;
                // Keep an apply failure visible until its retry window opens.
                if (nowUtc >= _retryAfterUtc) Publish(ApplicationUpdateStatus.Ready);
                return;
            }
            _idleSinceUtc ??= nowUtc;
            var restartAt = _idleSinceUtc.Value.AddSeconds(60);
            Publish(ApplicationUpdateStatus.Ready, restartAtUtc: restartAt);
            if (nowUtc >= restartAt) Restart(cancellationToken, automatic: true, nowUtc);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _idleSinceUtc = null;
            _failedAttempts = Math.Min(_failedAttempts + 1, 6);
            _retryAfterUtc = nowUtc.AddMinutes(Math.Min(360, 15 * (1 << (_failedAttempts - 1))));
            if (ex is ApplicationUpdateRetryException retry && retry.RetryAfterUtc > _serverRetryAfterUtc)
                _serverRetryAfterUtc = retry.RetryAfterUtc;
            if (_serverRetryAfterUtc > _retryAfterUtc) _retryAfterUtc = _serverRetryAfterUtc;
            if (Snapshot.Status == ApplicationUpdateStatus.Checking) _nextCheckUtc = _retryAfterUtc;
            Publish(ApplicationUpdateStatus.Failed, error: ex.Message);
            _log($"Application update failed; the current application continues running: {ex.Message}");
        }
    }

    private void Restart(CancellationToken cancellationToken, bool automatic, DateTime nowUtc)
    {
        lock (_sync)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (automatic && (!AutomaticAllowed(nowUtc) || !_canRestart())) return;
            _preserveSettings();
            cancellationToken.ThrowIfCancellationRequested();
            _backend.ScheduleRestart(_restartArguments());
            Publish(ApplicationUpdateStatus.Restarting);
        }
        _log($"Applying application update {_readyVersion}; settings backed up and session arguments retained.");
        _exit();
    }

    private bool AutomaticAllowed(DateTime nowUtc)
    {
        lock (_sync) return _checksEnabled && _preferences.AutomaticEnabled &&
            (_preferences.DeferredUntilUtc is null || nowUtc >= _preferences.DeferredUntilUtc);
    }

    private string? NewerVersion(string? value) => SemanticVersion.TryParse(value, out var parsed) &&
        parsed!.CompareTo(_currentVersion) > 0 && parsed.PreRelease is null ? parsed.ToString() : null;

    private void Publish(ApplicationUpdateStatus status, int progress = 0, DateTime? restartAtUtc = null, string? error = null)
    {
        lock (_sync) _snapshot = _snapshot with
        {
            Status = status, AvailableVersion = _readyVersion ?? _availableVersion,
            Progress = progress, RestartAtUtc = restartAtUtc, Error = error,
            RetryAfterUtc = status == ApplicationUpdateStatus.Failed ? _retryAfterUtc : null
        };
    }
}
