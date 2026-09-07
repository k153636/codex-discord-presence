using System.Windows.Forms;

namespace CodexDiscordPresence;

public static class PresenceApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            return InstanceCoordinator.StopRunningInstance(AppPaths.Create(AppProfileKind.Codex, AppContext.BaseDirectory));
        }

        var appPaths = AppPaths.Create(AppProfileKind.Codex, AppContext.BaseDirectory);
        AppDataInitializer.EnsureInitialized(appPaths);
        using var diagnosticLog = DiagnosticLog.Create(appPaths.LogsDirectory);

        var options = AppOptions.Load(args, appPaths);

        using var cts = new CancellationTokenSource();
        using var instance = InstanceCoordinator.TryAcquire(appPaths);

        if (instance is null)
        {
            diagnosticLog.Error("Codex Discord RPC is already running. Use --stop to end the current instance.");
            return 1;
        }

        diagnosticLog.Info($"Startup: profile={appPaths.Profile}, baseDirectory={appPaths.BaseDirectory}, logFile={diagnosticLog.Path}");

        TrayIconHost? trayHost = null;
        SynchronizationContext? uiSynchronizationContext = null;
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            if (uiSynchronizationContext is not null)
            {
                uiSynchronizationContext.Post(_ => trayHost?.RequestExit(), null);
            }
            else
            {
                trayHost?.RequestExit();
            }
        };

        var stateStore = new PresenceStateStore();
        var statePath = appPaths.StatePath;
        var runtimeState = stateStore.Load(statePath);
        var settingsPath = appPaths.ExecutableSettingsPath;
        var runtime = new PresenceRuntime(options, runtimeState, cts.Token, appPaths, diagnosticLog);

        trayHost = new TrayIconHost(runtimeState, stateStore, statePath, settingsPath, () => cts.Cancel());
        var activeUiSynchronizationContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        uiSynchronizationContext = activeUiSynchronizationContext;
        SynchronizationContext.SetSynchronizationContext(activeUiSynchronizationContext);
        // Keep the WinForms message loop responsive while session discovery, Git inspection,
        // and Discord RPC initialization run. RunAsync does not access WinForms controls.
        var runtimeTask = Task.Run(() => runtime.RunAsync());
        var updateCheckTask = options.EnableUpdateCheck
            ? Task.Run(() => CheckForUpdatesAsync(cts.Token, diagnosticLog))
            : Task.CompletedTask;

        _ = runtimeTask.ContinueWith(
            _ => activeUiSynchronizationContext.Post(context => trayHost?.RequestExit(), null),
            TaskScheduler.Default);

        diagnosticLog.Info("Codex Discord RPC is running in the background.");
        diagnosticLog.Info("Right-click the tray icon for Enable, Edit Discord RPC, and Quit.");

        Application.Run(trayHost);

        cts.Cancel();
        trayHost.RequestExit();

        try
        {
            await runtimeTask;
            await updateCheckTask;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            diagnosticLog.Error("Presence runtime failed", ex);
            return 1;
        }

        return 0;
    }

    private static async Task CheckForUpdatesAsync(CancellationToken cancellationToken, DiagnosticLog diagnosticLog)
    {
        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var releaseChecker = new GitHubReleaseChecker(httpClient);
            var releaseCheck = await releaseChecker.CheckLatestReleaseAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!releaseCheck.Succeeded)
            {
                diagnosticLog.Warn(releaseCheck.WarningMessage ?? "GitHub release check failed.");
            }
            else if (releaseCheck.UpdateAvailable && releaseCheck.LatestVersion is not null)
            {
                var releaseUrl = string.IsNullOrWhiteSpace(releaseCheck.LatestReleaseUrl)
                    ? string.Empty
                    : $" Release: {releaseCheck.LatestReleaseUrl}";

                diagnosticLog.Info(
                    $"GitHub release update available: current {releaseCheck.CurrentVersion} < latest {releaseCheck.LatestVersion}.{releaseUrl}");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            diagnosticLog.Warn($"GitHub release check failed: {ex.Message}");
        }
    }
}
