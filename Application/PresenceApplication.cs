using System.Windows.Forms;

namespace CodexDiscordPresence;

public static class PresenceApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 && args[0] == "--claude-spinner")
        {
            return ClaudeCodeSpinnerCommand.Run();
        }

        if (args.Length == 1 && args[0] == "--claude-hook")
        {
            return await ClaudeCodeHookCommand.RunAsync();
        }

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
            diagnosticLog.Error($"{ProductBrand.Name} is already running. Use --stop to end the current instance.");
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
        var runtimeState = stateStore.Load(statePath, options.Providers);
        var settingsPath = appPaths.UserSettingsPath;
        var sessionStartedAtUtc = UpdateRestartArguments.ReadSessionStart(args, DateTime.UtcNow);
        var runtime = new PresenceRuntime(options, runtimeState, cts.Token, appPaths, diagnosticLog, sessionStartedAtUtc);

        trayHost = new TrayIconHost(runtimeState, stateStore, statePath, settingsPath, () => cts.Cancel());
        var activeUiSynchronizationContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        uiSynchronizationContext = activeUiSynchronizationContext;
        SynchronizationContext.SetSynchronizationContext(activeUiSynchronizationContext);
        using var updateBackend = new VelopackUpdateBackend();
        var updates = new ApplicationUpdateCoordinator(updateBackend,
            new UpdatePreferencesStore(Path.Combine(appPaths.AppDataDirectory, "update-preferences.json")),
            () => UpdateRestartPolicy.CanRestart(runtimeState, trayHost.IsDashboardOpen, DateTime.UtcNow),
            new UpdateSettingsPreserver(appPaths).Preserve,
            () => UpdateRestartArguments.Build(args, runtimeState.DashboardSnapshot.PublishedPresence?.StartedAtUtc ?? sessionStartedAtUtc),
            () => activeUiSynchronizationContext.Post(_ => trayHost.RequestExit(), null),
            diagnosticLog.Warn, options.EnableUpdateCheck);
        trayHost.AttachUpdates(updates);
        diagnosticLog.Info($"Update service: version={AppVersion.Current}, managedInstallation={updateBackend.IsInstalled}, automatic={updates.Snapshot.AutomaticEnabled}");
        // Keep the WinForms message loop responsive while session discovery, Git inspection,
        // and Discord RPC initialization run. RunAsync does not access WinForms controls.
        var runtimeTask = Task.Run(() => runtime.RunAsync());
        var updateCheckTask = Task.Run(() => updates.RunAsync(cts.Token));

        _ = runtimeTask.ContinueWith(
            _ => activeUiSynchronizationContext.Post(context => trayHost?.RequestExit(), null),
            TaskScheduler.Default);

        diagnosticLog.Info($"{ProductBrand.Name} is running in the background.");
        diagnosticLog.Info($"Right-click the tray icon for Enable, Edit {ProductBrand.Name} settings, and Quit.");

        Application.Run(trayHost);

        cts.Cancel();
        trayHost.RequestExit();

        try
        {
            // The message loop has ended. Drain background work on this owner thread:
            // posting an await continuation to the closed WinForms loop would strand
            // shutdown, and the single-instance mutex must be released by its owner.
            SynchronizationContext.SetSynchronizationContext(null);
            Task.WhenAll(runtimeTask, updateCheckTask).GetAwaiter().GetResult();
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
