using CodexDiscordPresence;

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

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cts.Cancel();
};

var session = new SessionClock();
var codexDetector = new CodexProcessDetector(options.Codex, options.Presence);
var modelNameProvider = new CodexModelNameProvider(options.Codex, options.Presence);
var projectInspector = new ProjectInspector(options.Project);
var gitInspector = new GitInspector();
var tokenUsageProvider = new TokenUsageProvider(options.TokenUsage);
var renderer = new PresenceTemplateRenderer();
var rpc = new DiscordPresenceClient(options.Discord);

Console.WriteLine("Starting Codex Discord RPC.");
Console.WriteLine($"Project path: {projectInspector.ProjectPath}");
Console.WriteLine("Press Ctrl+C to stop.");

await rpc.StartAsync(cts.Token);

ModelNameSnapshot? lastModelSnapshot = null;
CodexProcessSnapshot? lastActivitySnapshot = null;
string? lastPresenceDetails = null;
string? lastPresenceState = null;

while (!cts.IsCancellationRequested)
{
    try
    {
        var projectSnapshot = projectInspector.GetSnapshot();
        var gitSnapshot = gitInspector.GetSnapshot(projectInspector.ProjectPath);
        var codexSnapshot = codexDetector.GetSnapshot(projectInspector.ProjectPath, projectSnapshot, gitSnapshot);
        var modelSnapshot = modelNameProvider.GetSnapshot(projectInspector.ProjectPath);
        if (lastModelSnapshot is null ||
            !string.Equals(modelSnapshot.SelectedUiModel, lastModelSnapshot.SelectedUiModel, StringComparison.Ordinal) ||
            !string.Equals(modelSnapshot.LastUsedSessionModel, lastModelSnapshot.LastUsedSessionModel, StringComparison.Ordinal) ||
            !string.Equals(modelSnapshot.FinalDisplayedModel, lastModelSnapshot.FinalDisplayedModel, StringComparison.Ordinal))
        {
            Console.WriteLine(
                "Model detection: " +
                $"Selected UI model={FormatLogValue(modelSnapshot.SelectedUiModel)}, " +
                $"Last used session model={FormatLogValue(modelSnapshot.LastUsedSessionModel)}, " +
                $"Final displayed model={FormatLogValue(modelSnapshot.FinalDisplayedModel)} " +
                $"(source={modelSnapshot.Source})");
            lastModelSnapshot = modelSnapshot;
        }

        if (lastActivitySnapshot is null ||
            lastActivitySnapshot.ActivityKind != codexSnapshot.ActivityKind ||
            lastActivitySnapshot.ActivityProvenance != codexSnapshot.ActivityProvenance ||
            !string.Equals(lastActivitySnapshot.ActivityReason, codexSnapshot.ActivityReason, StringComparison.Ordinal))
        {
            Console.WriteLine(
                "Activity detection: " +
                $"state={codexSnapshot.ActivityKind}, " +
                $"provenance={codexSnapshot.ActivityProvenance}, " +
                $"reason={codexSnapshot.ActivityReason}");
            lastActivitySnapshot = codexSnapshot;
        }

        var tokenSnapshot = tokenUsageProvider.GetSnapshot();

        var context = new PresenceContext(
            modelSnapshot.FinalDisplayedModel,
            codexSnapshot,
            projectSnapshot,
            gitSnapshot,
            session.GetSnapshot(),
            tokenSnapshot);

        var presence = renderer.Render(options.Presence, context);
        if (!string.Equals(presence.Details, lastPresenceDetails, StringComparison.Ordinal) ||
            !string.Equals(presence.State, lastPresenceState, StringComparison.Ordinal))
        {
            Console.WriteLine($"Presence rendered: Details={presence.Details}; State={presence.State}");
            lastPresenceDetails = presence.Details;
            lastPresenceState = presence.State;
        }

        rpc.Update(presence);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Presence update loop failed: {ex.Message}");
    }

    await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.UpdateIntervalSeconds)), cts.Token)
        .ContinueWith(_ => { }, CancellationToken.None);
}

rpc.Clear();
Console.WriteLine("Stopped Codex Discord RPC.");
return 0;

static string FormatLogValue(string? value)
{
    return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
}
