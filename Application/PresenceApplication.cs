using System.Windows.Forms;

namespace CodexDiscordPresence;

public static class PresenceApplication
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Any(arg => string.Equals(arg, "--stop", StringComparison.OrdinalIgnoreCase)))
        {
            return InstanceCoordinator.StopRunningInstance(AppContext.BaseDirectory);
        }

        var options = AppOptions.Load(args);

        if (string.IsNullOrWhiteSpace(options.Discord.ClientId) ||
            options.Discord.ClientId == "YOUR_DISCORD_APPLICATION_CLIENT_ID")
        {
            Console.Error.WriteLine("Set Discord:ClientId in appsettings.json or pass --client-id <id>.");
            return 1;
        }

        using var cts = new CancellationTokenSource();
        using var instance = InstanceCoordinator.TryAcquire(AppContext.BaseDirectory);

        if (instance is null)
        {
            Console.Error.WriteLine("Codex Discord RPC is already running. Use --stop to end the current instance.");
            return 1;
        }

        TrayIconHost? trayHost = null;
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cts.Cancel();
            trayHost?.RequestExit();
        };

        var runtimeState = new PresenceRuntimeState();
        var runtime = new PresenceRuntime(options, runtimeState, cts.Token);
        var runtimeTask = runtime.RunAsync();

        var settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
        trayHost = new TrayIconHost(runtimeState, settingsPath, () => cts.Cancel());
        _ = runtimeTask.ContinueWith(_ => trayHost?.RequestExit(), TaskScheduler.Default);

        Console.WriteLine("Codex Discord RPC is running in the background.");
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
}
