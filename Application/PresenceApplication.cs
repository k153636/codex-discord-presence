using CodexDiscordPresence;

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
        var tokenUsageProvider = new TokenUsageProvider(options.Codex, options.TokenUsage);
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
        string? stableCostModelName = null;
        var lastActivityKind = CodexActivityKind.Ready;
        var lastAnalyzingRepeatCount = 1;
        DateTime? lastAnalyzingTaskStartedAt = null;
        DateTime? lastAnalyzingStartedAt = null;
        DateTime? lastActivityStartedAt = null;
        string? lastPresenceSignature = null;
        var lastSuccessfulUpdateUtc = DateTime.MinValue;
        var keepAliveInterval = TimeSpan.FromSeconds(15);

        while (!cts.IsCancellationRequested)
        {
            try
            {
                var projectSnapshot = projectInspector.GetSnapshot();
                var gitSnapshot = gitInspector.GetSnapshot(projectInspector.ProjectPath);
                var codexSnapshot = codexDetector.GetSnapshot(projectInspector.ProjectPath, projectSnapshot, gitSnapshot, lastActivityKind);
                var analyzingRepeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
                    codexSnapshot.ActivityKind,
                    lastActivityKind,
                    codexSnapshot.LastTaskStartedAt,
                    lastAnalyzingTaskStartedAt,
                    lastAnalyzingRepeatCount);
                codexSnapshot = codexSnapshot with { ActivityRepeatCount = analyzingRepeatCount };
                codexSnapshot = codexSnapshot with
                {
                    ActivityStartedAt = ResolveActivityStartedAt(
                        codexSnapshot.ActivityKind,
                        lastActivityKind,
                        lastActivityStartedAt,
                        lastAnalyzingStartedAt,
                        codexSnapshot.LastObservedAt,
                        options.Presence.RunningCommandHoldSeconds)
                };
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

                if (stableCostModelName is null &&
                    !string.IsNullOrWhiteSpace(modelSnapshot.FinalDisplayedModel))
                {
                    stableCostModelName = modelSnapshot.FinalDisplayedModel;
                }

                if (lastActivitySnapshot is null ||
                    lastActivitySnapshot.ActivityKind != codexSnapshot.ActivityKind ||
                    lastActivitySnapshot.ActivityProvenance != codexSnapshot.ActivityProvenance ||
                    !string.Equals(lastActivitySnapshot.ActivityReason, codexSnapshot.ActivityReason, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        "Activity detection: " +
                        $"state={codexSnapshot.ActivityKind}, " +
                        $"confidence={codexSnapshot.Confidence}, " +
                        $"provenance={codexSnapshot.ActivityProvenance}, " +
                        $"reason={codexSnapshot.ActivityReason}");
                    lastActivitySnapshot = codexSnapshot;
                }

                var context = new PresenceContext(
                    modelSnapshot.FinalDisplayedModel,
                    codexSnapshot,
                    projectSnapshot,
                    gitSnapshot,
                    session.GetSnapshot(),
                    tokenUsageProvider.GetSnapshot(projectInspector.ProjectPath, stableCostModelName));

                var presence = renderer.Render(options.Presence, context);
                var presenceSignature = BuildPresenceSignature(presence);
                var keepAliveDue = PresenceUpdatePolicy.ShouldSendKeepAlive(lastSuccessfulUpdateUtc, DateTime.UtcNow, keepAliveInterval);
                if (!string.Equals(presence.Details, lastPresenceDetails, StringComparison.Ordinal) ||
                    !string.Equals(presence.State, lastPresenceState, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"Presence rendered: Details={FormatLogValueForMultiline(presence.Details)}; " +
                        $"State={FormatLogValueForMultiline(presence.State)}");
                    lastPresenceDetails = presence.Details;
                    lastPresenceState = presence.State;
                }

                if (!string.Equals(presenceSignature, lastPresenceSignature, StringComparison.Ordinal) || keepAliveDue)
                {
                    if (rpc.Update(presence))
                    {
                        lastPresenceSignature = presenceSignature;
                        lastSuccessfulUpdateUtc = DateTime.UtcNow;
                    }
                }

                lastAnalyzingRepeatCount = analyzingRepeatCount;
                lastAnalyzingTaskStartedAt = codexSnapshot.ActivityKind == CodexActivityKind.AnalyzingProject
                    ? codexSnapshot.LastTaskStartedAt
                    : null;
                if (codexSnapshot.ActivityKind == CodexActivityKind.AnalyzingProject)
                {
                    lastAnalyzingStartedAt = codexSnapshot.ActivityStartedAt;
                }
                lastActivityStartedAt = codexSnapshot.ActivityStartedAt;
                lastActivityKind = codexSnapshot.ActivityKind;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Presence update loop failed: {ex.Message}");
            }

            var delay = PresenceRefreshPolicy.GetNextDelay(options.Presence, lastActivityKind, options.UpdateIntervalSeconds);

            await Task.Delay(delay, cts.Token)
                .ContinueWith(_ => { }, CancellationToken.None);
        }

        rpc.Clear();
        Console.WriteLine("Stopped Codex Discord RPC.");
        return 0;
    }

    private static string FormatLogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<none>" : value;
    }

    private static string FormatLogValueForMultiline(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "<none>"
            : value.ReplaceLineEndings("\\n");
    }

    private static string BuildPresenceSignature(RenderedPresence presence)
    {
        var buttons = string.Join(
            "|",
            presence.Buttons.Select(button => $"{button.Label}=>{button.Url}"));

        return string.Join(
            "\u001f",
            presence.Details,
            presence.State,
            presence.LargeImageText,
            presence.SmallImageText,
            buttons);
    }

    private static DateTime? ResolveActivityStartedAt(
        CodexActivityKind currentActivityKind,
        CodexActivityKind lastActivityKind,
        DateTime? lastActivityStartedAt,
        DateTime? lastAnalyzingStartedAt,
        DateTime? currentObservedAt,
        int runningCommandHoldSeconds)
    {
        if (!currentActivityKind.IsActive())
        {
            return null;
        }

        if (currentActivityKind == CodexActivityKind.AnalyzingProject &&
            lastAnalyzingStartedAt.HasValue &&
            lastActivityKind == CodexActivityKind.RunningCommand &&
            lastActivityStartedAt.HasValue &&
            (!currentObservedAt.HasValue ||
             currentObservedAt.Value - lastActivityStartedAt.Value <= TimeSpan.FromSeconds(Math.Max(1, runningCommandHoldSeconds))))
        {
            return lastAnalyzingStartedAt;
        }

        if (currentActivityKind == lastActivityKind && lastActivityStartedAt.HasValue)
        {
            return lastActivityStartedAt;
        }

        return currentObservedAt ?? DateTime.UtcNow;
    }
}
