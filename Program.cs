using CodexDiscordPresence;

var options = AppOptions.Load(args);

if (string.IsNullOrWhiteSpace(options.Discord.ClientId) ||
    options.Discord.ClientId == "YOUR_DISCORD_APPLICATION_CLIENT_ID")
{
    Console.Error.WriteLine("Set Discord:ClientId in appsettings.json or pass --client-id <id>.");
    return 1;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var session = new SessionClock();
var codexDetector = new CodexProcessDetector(options.Codex);
var projectInspector = new ProjectInspector(options.Project);
var gitInspector = new GitInspector();
var tokenUsageProvider = new TokenUsageProvider(options.TokenUsage);
var renderer = new PresenceTemplateRenderer();
var rpc = new DiscordPresenceClient(options.Discord);

Console.WriteLine("Starting Codex Discord RPC.");
Console.WriteLine($"Project path: {projectInspector.ProjectPath}");
Console.WriteLine("Press Ctrl+C to stop.");

await rpc.StartAsync(cts.Token);

while (!cts.IsCancellationRequested)
{
    var projectSnapshot = projectInspector.GetSnapshot();
    var gitSnapshot = gitInspector.GetSnapshot(projectInspector.ProjectPath);
    var codexSnapshot = codexDetector.GetSnapshot();
    var tokenSnapshot = tokenUsageProvider.GetSnapshot();

    var context = new PresenceContext(
        codexSnapshot,
        projectSnapshot,
        gitSnapshot,
        session.GetSnapshot(),
        tokenSnapshot);

    var presence = renderer.Render(options.Presence, context);
    rpc.Update(presence);

    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.UpdateIntervalSeconds)), cts.Token)
        .ContinueWith(_ => { }, CancellationToken.None);
}

rpc.Clear();
Console.WriteLine("Stopped Codex Discord RPC.");
return 0;
