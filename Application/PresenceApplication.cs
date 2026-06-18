using System.Windows.Forms;

namespace CodexDiscordPresence;

public static class PresenceApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        var profile = IsCliProfileRequested(args) ? AppProfileKind.CodexCli : AppProfileKind.Codex;

        if (args.Any(arg => string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            return InstanceCoordinator.StopRunningInstance(AppPaths.Create(profile, AppContext.BaseDirectory));
        }

        var appPaths = AppPaths.Create(profile, AppContext.BaseDirectory);
        AppDataInitializer.EnsureInitialized(appPaths);

        var options = AppOptions.Load(args, appPaths);

        if (IsMissingDiscordClientId(options.Discord.ClientId))
        {
            Console.Error.WriteLine(
                profile == AppProfileKind.CodexCli
                    ? "Set Discord:ClientId in appsettings.cli.json or pass --client-id <id>."
                    : "Set Discord:ClientId in appsettings.json or pass --client-id <id>.");
        }

        using var cts = new CancellationTokenSource();
        using var instance = InstanceCoordinator.TryAcquire(appPaths);

        if (instance is null)
        {
            Console.Error.WriteLine(
                profile == AppProfileKind.CodexCli
                    ? "Codex CLI Discord RPC is already running. Use --stop to end the current instance."
                    : "Codex Discord RPC is already running. Use --stop to end the current instance.");
            return 1;
        }

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
                Console.Error.WriteLine(releaseCheck.WarningMessage);
            }
            else if (releaseCheck.UpdateAvailable && releaseCheck.LatestVersion is not null)
            {
                var releaseUrl = string.IsNullOrWhiteSpace(releaseCheck.LatestReleaseUrl)
                    ? string.Empty
                    : $" Release: {releaseCheck.LatestReleaseUrl}";

                Console.WriteLine(
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
        var settingsPath = appPaths.ExecutableSettingsPath;
        var runtime = new PresenceRuntime(options, runtimeState, cts.Token, appPaths);
        var runtimeTask = runtime.RunAsync();

        trayHost = new TrayIconHost(runtimeState, stateStore, statePath, settingsPath, () => cts.Cancel());
        _ = runtimeTask.ContinueWith(_ => trayHost?.RequestExit(), TaskScheduler.Default);

        Console.WriteLine(
            profile == AppProfileKind.CodexCli
                ? "Codex CLI Discord RPC is running in the background."
                : "Codex Discord RPC is running in the background.");
        Console.WriteLine("Right-click the tray icon for Enable, Edit Discord RPC, and Quit.");

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
            Console.Error.WriteLine($"Presence runtime failed: {ex.Message}");
            return 1;
        }

        return 0;
    }

    private static bool IsCliProfileRequested(IEnumerable<string> args)
    {
        var argList = args as string[] ?? args.ToArray();
        for (var i = 0; i < argList.Length; i++)
        {
            if (string.Equals(argList[i], "--cli", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (string.Equals(argList[i], "--profile", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < argList.Length &&
                string.Equals(argList[i + 1], "cli", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMissingDiscordClientId(string? clientId)
    {
        return string.IsNullOrWhiteSpace(clientId) ||
            clientId.StartsWith("YOUR_", StringComparison.OrdinalIgnoreCase);
    }
}
