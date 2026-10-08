using CodexDiscordPresence;

namespace CodexDiscordPresence;

public static class Program
{
    internal static bool RestartedAfterUpdate { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnRestarted(_ => RestartedAfterUpdate = true).Run();
        return PresenceApplication.RunAsync(args).GetAwaiter().GetResult();
    }
}
