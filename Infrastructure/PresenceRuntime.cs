namespace CodexDiscordPresence;

public sealed class PresenceRuntime
{
    private readonly AppOptions _options;
    private readonly PresenceRuntimeState _state;
    private readonly CancellationToken _cancellationToken;
    private readonly AppPaths _paths;
    private RuntimeTimingSettings _timingSettings;
    private DateTime _executableSettingsLastWriteTimeUtc;
    private DateTime _userSettingsLastWriteTimeUtc;

    public PresenceRuntime(AppOptions options, PresenceRuntimeState state, CancellationToken cancellationToken, AppPaths paths)
    {
        _options = options;
        _state = state;
        _cancellationToken = cancellationToken;
        _paths = paths;
        _timingSettings = RuntimeTimingSettings.From(options);
        _executableSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.ExecutableSettingsPath);
        _userSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.UserSettingsPath);
    }

    public async Task RunAsync()
    {
        var session = new SessionClock();
        var codexOptions = _options.GetCodexDetectionOptions(_paths.Profile);
        var codexDetector = new CodexProcessDetector(codexOptions, _options.Presence);
        var modelNameProvider = new CodexModelNameProvider(codexOptions, _options.Presence);
        var projectInspector = new ProjectInspector(_options.Project);
        var gitInspector = new GitInspector();
        var tokenUsageProvider = new TokenUsageProvider(codexOptions, _options.TokenUsage);
        var renderer = new PresenceTemplateRenderer();
        var rpc = new DiscordPresenceClient(_options.Discord);
        var projectSwitchDetectionInterval = TimeSpan.FromSeconds(3);

        Console.WriteLine("Starting Codex Discord RPC.");
        var activeProjectPath = projectInspector.ProjectPath;
        Console.WriteLine($"Project path: {activeProjectPath}");
        Console.WriteLine("Press Ctrl+C or Quit to stop.");

        await rpc.StartAsync(_cancellationToken);

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
        var lastLoggedProjectPath = activeProjectPath;
        var wasDisabled = false;

        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                RefreshTimingSettingsIfNeeded();

                if (!_state.Enabled)
                {
                    if (!wasDisabled)
                    {
                        rpc.Clear();
                        wasDisabled = true;
                        Console.WriteLine("Presence disabled.");
                    }

                    await Delay(TimeSpan.FromSeconds(1));
                    continue;
                }

                if (wasDisabled)
                {
                    Console.WriteLine("Presence enabled.");
                    lastModelSnapshot = null;
                    lastActivitySnapshot = null;
                    stableCostModelName = null;
                    lastPresenceSignature = null;
                    lastPresenceDetails = null;
                    lastPresenceState = null;
                    lastAnalyzingRepeatCount = 1;
                    lastAnalyzingTaskStartedAt = null;
                    lastAnalyzingStartedAt = null;
                    lastActivityStartedAt = null;
                    lastActivityKind = CodexActivityKind.Ready;
                    wasDisabled = false;
                }

                var observedProjectPath = codexDetector.GetObservedProjectPath(activeProjectPath);
                if (!string.IsNullOrWhiteSpace(observedProjectPath))
                {
                    activeProjectPath = projectInspector.NormalizeProjectPath(observedProjectPath);
                }

                if (!string.Equals(activeProjectPath, lastLoggedProjectPath, StringComparison.OrdinalIgnoreCase))
                {
                    Console.WriteLine($"Project switched: {lastLoggedProjectPath} -> {activeProjectPath}");
                    lastLoggedProjectPath = activeProjectPath;
                    lastModelSnapshot = null;
                    lastActivitySnapshot = null;
                    stableCostModelName = null;
                    lastPresenceSignature = null;
                    lastPresenceDetails = null;
                    lastPresenceState = null;
                    lastAnalyzingRepeatCount = 1;
                    lastAnalyzingTaskStartedAt = null;
                    lastAnalyzingStartedAt = null;
                    lastActivityStartedAt = null;
                    lastActivityKind = CodexActivityKind.Ready;
                }

                var projectSnapshot = projectInspector.GetSnapshot(activeProjectPath);
                var gitSnapshot = gitInspector.GetSnapshot(activeProjectPath);
                var codexSnapshot = codexDetector.GetSnapshot(activeProjectPath, projectSnapshot, gitSnapshot, lastActivityKind);
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
                        _options.Presence.RunningCommandHoldSeconds)
                };
                var modelSnapshot = modelNameProvider.GetSnapshot(activeProjectPath);
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
                    tokenUsageProvider.GetSnapshot(activeProjectPath, stableCostModelName));

                var presence = renderer.Render(_options.Presence, context);
                var presenceSignature = BuildPresenceSignature(presence);
                var keepAliveDue = PresenceUpdatePolicy.ShouldSendKeepAlive(lastSuccessfulUpdateUtc, DateTime.UtcNow, keepAliveInterval);
                var shouldSendPresence = PresenceDispatchPolicy.ShouldSendPresence(
                    presenceSignature,
                    lastPresenceSignature,
                    keepAliveDue,
                    rpc.NeedsPresenceRefresh);
                if (!string.Equals(presence.Details, lastPresenceDetails, StringComparison.Ordinal) ||
                    !string.Equals(presence.State, lastPresenceState, StringComparison.Ordinal))
                {
                    Console.WriteLine(
                        $"Presence rendered: Details={FormatLogValueForMultiline(presence.Details)}; " +
                        $"State={FormatLogValueForMultiline(presence.State)}");
                    lastPresenceDetails = presence.Details;
                    lastPresenceState = presence.State;
                }

                if (shouldSendPresence)
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
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Presence update loop failed: {ex.Message}");
            }

            var delay = PresenceRefreshPolicy.GetNextDelay(_options.Presence, lastActivityKind, _options.UpdateIntervalSeconds);
            if (delay > projectSwitchDetectionInterval)
            {
                delay = projectSwitchDetectionInterval;
            }

            await Delay(delay);
        }

        rpc.Clear();
        Console.WriteLine("Stopped Codex Discord RPC.");
    }

    private async Task Delay(TimeSpan delay)
    {
        try
        {
            await Task.Delay(delay, _cancellationToken);
        }
        catch (OperationCanceledException)
        {
        }
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

    private void RefreshTimingSettingsIfNeeded()
    {
        var executableLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.ExecutableSettingsPath);
        var userLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.UserSettingsPath);
        if (executableLastWriteTimeUtc == _executableSettingsLastWriteTimeUtc &&
            userLastWriteTimeUtc == _userSettingsLastWriteTimeUtc)
        {
            return;
        }

        _executableSettingsLastWriteTimeUtc = executableLastWriteTimeUtc;
        _userSettingsLastWriteTimeUtc = userLastWriteTimeUtc;

        try
        {
            var reloadedOptions = AppOptions.LoadMerged(_paths.ExecutableSettingsPath, _paths.UserSettingsPath);
            var reloadedTiming = RuntimeTimingSettings.From(reloadedOptions);

            if (!reloadedTiming.Equals(_timingSettings))
            {
                _timingSettings = reloadedTiming;
                _timingSettings.ApplyTo(_options);
                Console.WriteLine(
                    "Timing settings reloaded: " +
                    $"UpdateIntervalSeconds={_options.UpdateIntervalSeconds}, " +
                    $"ActiveUpdateIntervalSeconds={_options.Presence.ActiveUpdateIntervalSeconds}, " +
                    $"RunningCommandUpdateIntervalSeconds={_options.Presence.RunningCommandUpdateIntervalSeconds}, " +
                    $"RunningCommandUpdateIntervalMilliseconds={_options.Presence.RunningCommandUpdateIntervalMilliseconds}, " +
                    $"IdleUpdateIntervalSeconds={_options.Presence.IdleUpdateIntervalSeconds}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to reload timing settings: {ex.Message}");
        }
    }

    private static DateTime GetSettingsLastWriteTimeUtc(string path)
    {
        try
        {
            return File.Exists(path)
                ? File.GetLastWriteTimeUtc(path)
                : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }
}
