namespace CodexDiscordPresence;

internal sealed class AntigravityIntegrationCoordinator
{
    private readonly AntigravityStatusLineInstaller _statusLineInstaller;
    private readonly AntigravityHookInstaller _hookInstaller;
    private readonly DiagnosticLog _log;
    private bool _statusLineInstalled;
    private bool _hookInstalled;
    private bool _statusLineConflictLogged;
    private bool _hookConflictLogged;

    internal AntigravityIntegrationCoordinator(
        AntigravityStatusLineInstaller statusLineInstaller,
        AntigravityHookInstaller hookInstaller,
        DiagnosticLog log)
    {
        _statusLineInstaller = statusLineInstaller ?? throw new ArgumentNullException(nameof(statusLineInstaller));
        _hookInstaller = hookInstaller ?? throw new ArgumentNullException(nameof(hookInstaller));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    internal bool Sync(bool enabled)
    {
        if (!enabled)
        {
            UninstallIfNeeded();
            _statusLineConflictLogged = false;
            _hookConflictLogged = false;
            return false;
        }

        EnsureStatusLineInstalled();
        EnsureHookInstalled();
        return _statusLineInstalled || _hookInstalled;
    }

    internal void UninstallIfNeeded()
    {
        if (_hookInstalled)
        {
            var result = _hookInstaller.Uninstall();
            if (!result.Succeeded)
            {
                _log.Warn(
                    $"Antigravity hook cleanup did not complete: " +
                    $"{result.Message ?? result.Status.ToString()}.");
            }

            _hookInstalled = false;
        }

        if (_statusLineInstalled)
        {
            var result = _statusLineInstaller.Uninstall();
            if (!result.Succeeded)
            {
                _log.Warn(
                    $"Antigravity statusLine cleanup did not complete: " +
                    $"{result.Message ?? result.Status.ToString()}.");
            }

            _statusLineInstalled = false;
        }
    }

    private void EnsureStatusLineInstalled()
    {
        if (_statusLineInstalled)
        {
            return;
        }

        var result = _statusLineInstaller.Install();
        if (result.Status is
            AntigravityStatusLineOperationStatus.Installed or
            AntigravityStatusLineOperationStatus.AlreadyInstalled)
        {
            _statusLineInstalled = true;
            _statusLineConflictLogged = false;
            _log.Info($"Antigravity statusLine integration: {result.Status}.");
            return;
        }

        if (!_statusLineConflictLogged)
        {
            _log.Warn(
                $"Antigravity statusLine integration is unavailable: " +
                $"{result.Message ?? result.Status.ToString()}.");
            _statusLineConflictLogged = true;
        }
    }

    private void EnsureHookInstalled()
    {
        if (_hookInstalled)
        {
            return;
        }

        var result = _hookInstaller.Install();
        if (result.Status is
            AntigravityHookOperationStatus.Installed or
            AntigravityHookOperationStatus.AlreadyInstalled)
        {
            _hookInstalled = true;
            _hookConflictLogged = false;
            _log.Info($"Antigravity hooks integration: {result.Status}.");
            return;
        }

        if (!_hookConflictLogged)
        {
            _log.Warn(
                $"Antigravity hooks integration is unavailable: " +
                $"{result.Message ?? result.Status.ToString()}.");
            _hookConflictLogged = true;
        }
    }
}
