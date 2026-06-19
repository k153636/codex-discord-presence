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
        if (options.EnableUpdateCheck)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(5)
            };

            var releaseChecker = new GitHubReleaseChecker(httpClient);
            var releaseCheck = await releaseChecker.CheckLatestReleaseAsync(cts.Token);
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

        TrayIconHost? trayHost = null;
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            trayHost?.RequestExit();
        };

        var stateStore = new PresenceStateStore();
        var statePath = appPaths.StatePath;
        var runtimeState = stateStore.Load(statePath);
        if (!runtimeState.SessionStartedAtUtc.HasValue)
        {
            runtimeState.SessionStartedAtUtc = DateTime.UtcNow;
            stateStore.Save(statePath, runtimeState);
        }
        var settingsPath = appPaths.ExecutableSettingsPath;
        var runtime = new PresenceRuntime(options, runtimeState, cts.Token, appPaths, diagnosticLog);
        var runtimeTask = runtime.RunAsync();

        trayHost = new TrayIconHost(runtimeState, stateStore, statePath, settingsPath, () => cts.Cancel());
        _ = runtimeTask.ContinueWith(_ => trayHost?.RequestExit(), TaskScheduler.Default);

        diagnosticLog.Info("Codex Discord RPC is running in the background.");
        diagnosticLog.Info("Right-click the tray icon for Enable, Edit Discord RPC, and Quit.");

        Application.Run(trayHost);

        cts.Cancel();
        trayHost.RequestExit();

        try
        {
            await runtimeTask;
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
}
