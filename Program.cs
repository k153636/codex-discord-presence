using CodexDiscordPresence;

namespace CodexDiscordPresence;

public static class Program
{
    [STAThread]
    public static async Task<int> Main(string[] args)
    {
        return await PresenceApplication.RunAsync(args);
    }
}
