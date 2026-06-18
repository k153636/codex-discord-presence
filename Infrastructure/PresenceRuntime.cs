namespace CodexDiscordPresence;

public sealed class PresenceRuntime
{
    private readonly AppOptions _options;
    private readonly PresenceRuntimeState _state;
    private readonly CancellationToken _cancellationToken;
    private readonly AppPaths _paths;
    private RuntimeTimingSettings _timingSettings;
    private DateTime _executableSettingsLastWriteTimeUtc;
    private DateTime _cliSettingsLastWriteTimeUtc;
    private DateTime _userSettingsLastWriteTimeUtc;

    public PresenceRuntime(AppOptions options, PresenceRuntimeState state, CancellationToken cancellationToken, AppPaths paths)
    {
        _options = options;
        _state = state;
        _cancellationToken = cancellationToken;
        _paths = paths;
        _timingSettings = RuntimeTimingSettings.From(options);
        _executableSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.ExecutableSettingsPath);
        _cliSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(Path.Combine(_paths.BaseDirectory, SettingsFileNames.Cli));
        _userSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.UserSettingsPath);
    }

    public async Task RunAsync()
    {
        var session = new SessionClock();
        var profileStates = BuildProfileStates();
        var projectInspector = new ProjectInspector(_options.Project);
        var gitInspector = new GitInspector();
        var renderer = new PresenceTemplateRenderer();
        var projectSwitchDetectionInterval = TimeSpan.FromSeconds(3);

        Console.WriteLine("Starting Codex Discord RPC with auto-detection.");
        var activeProjectPath = projectInspector.ProjectPath;
        Console.WriteLine($"Project path: {activeProjectPath}");
        Console.WriteLine("Press Ctrl+C or Quit to stop.");

        var initialCodexProbe = profileStates[AppProfileKind.Codex].Detector.GetSnapshot(activeProjectPath);
        var initialCliProbe = profileStates[AppProfileKind.CodexCli].Detector.GetSnapshot(activeProjectPath);
        var currentProfile = AppProfileSelectionPolicy.Select(
            AppProfileKind.Codex,
            new AppProfileSelectionCandidate(AppProfileKind.Codex, initialCodexProbe, profileStates[AppProfileKind.Codex].DiscordOptions),
            new AppProfileSelectionCandidate(AppProfileKind.CodexCli, initialCliProbe, profileStates[AppProfileKind.CodexCli].DiscordOptions));
        var rpc = new DiscordPresenceClient(profileStates[currentProfile].DiscordOptions);

        await rpc.StartAsync(_cancellationToken);

        var keepAliveInterval = TimeSpan.FromSeconds(15);
        var lastLoggedProjectPath = activeProjectPath;
        var wasDisabled = false;

        while (!_cancellationToken.IsCancellationRequested)
        {
            try
            {
                RefreshTimingSettingsIfNeeded();

                if (!HandleDisabledState(rpc, wasDisabled))
                {
                    wasDisabled = true;
                    await Delay(TimeSpan.FromSeconds(1));
                    continue;
                }

                if (wasDisabled)
                {
                    Console.WriteLine("Presence enabled.");
                    wasDisabled = false;
                    ResetAllProfilePresenceCaches(profileStates);
                }

                var observedCodexSnapshot = profileStates[AppProfileKind.Codex].Detector.GetSnapshot();
                var observedCliSnapshot = profileStates[AppProfileKind.CodexCli].Detector.GetSnapshot();

                var (nextProjectPath, projectPathChanged) = UpdateActiveProjectPath(
                    projectInspector,
                    activeProjectPath,
                    observedCodexSnapshot,
                    observedCliSnapshot,
                    ref lastLoggedProjectPath);
                activeProjectPath = nextProjectPath;

                if (projectPathChanged)
                {
                    ResetAllProfilePresenceCaches(profileStates);
                }

                var selectedProfile = SelectProfile(profileStates, activeProjectPath, currentProfile);
                var selectedProfileState = profileStates[selectedProfile];

                rpc.UpdateOptions(selectedProfileState.DiscordOptions);

                if (selectedProfile != currentProfile)
                {
                    Console.WriteLine($"Profile switched: {currentProfile} -> {selectedProfile}");
                    currentProfile = selectedProfile;
                }

                var projectSnapshot = projectInspector.GetSnapshot(activeProjectPath);
                var gitSnapshot = gitInspector.GetSnapshot(activeProjectPath);
                var codexSnapshot = BuildCodexSnapshot(
                    activeProjectPath,
                    projectSnapshot,
                    gitSnapshot,
                    selectedProfileState);
                var modelSnapshot = UpdateModelSnapshot(activeProjectPath, selectedProfileState);
                var context = BuildPresenceContext(
                    session,
                    activeProjectPath,
                    selectedProfileState,
                    modelSnapshot,
                    projectSnapshot,
                    gitSnapshot,
                    codexSnapshot);

                var presence = renderer.Render(_options.Presence, context);
                UpdateDiscordPresence(
                    rpc,
                    keepAliveInterval,
                    selectedProfileState,
                    presence);

                UpdateProfileActivityState(selectedProfileState, codexSnapshot);
            }
            catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Presence update loop failed: {ex.Message}");
            }

            var delay = PresenceRefreshPolicy.GetNextDelay(_options.Presence, profileStates[currentProfile].LastActivityKind, _options.UpdateIntervalSeconds);
            if (delay > projectSwitchDetectionInterval)
            {
                delay = projectSwitchDetectionInterval;
            }

            await Delay(delay);
        }

        rpc.Clear();
        Console.WriteLine("Stopped Codex Discord RPC.");
    }

    private bool HandleDisabledState(DiscordPresenceClient rpc, bool wasDisabled)
    {
        if (_state.Enabled)
        {
            return true;
        }

        if (!wasDisabled)
        {
            rpc.Clear();
            Console.WriteLine("Presence disabled.");
        }

        return false;
    }

    private static void ResetAllProfilePresenceCaches(Dictionary<AppProfileKind, ProfileRuntimeState> profileStates)
    {
        foreach (var profileState in profileStates.Values)
        {
            profileState.ResetPresenceCache();
        }
    }

    private static AppProfileKind SelectProfile(
        Dictionary<AppProfileKind, ProfileRuntimeState> profileStates,
        string activeProjectPath,
        AppProfileKind currentProfile)
    {
        var codexProfileSnapshot = profileStates[AppProfileKind.Codex].Detector.GetSnapshot(activeProjectPath);
        var cliProfileSnapshot = profileStates[AppProfileKind.CodexCli].Detector.GetSnapshot(activeProjectPath);
        return AppProfileSelectionPolicy.Select(
            currentProfile,
            new AppProfileSelectionCandidate(AppProfileKind.Codex, codexProfileSnapshot, profileStates[AppProfileKind.Codex].DiscordOptions),
            new AppProfileSelectionCandidate(AppProfileKind.CodexCli, cliProfileSnapshot, profileStates[AppProfileKind.CodexCli].DiscordOptions));
    }

    private static (string ActiveProjectPath, bool Changed) UpdateActiveProjectPath(
        ProjectInspector projectInspector,
        string activeProjectPath,
        CodexProcessSnapshot observedCodexSnapshot,
        CodexProcessSnapshot observedCliSnapshot,
        ref string lastLoggedProjectPath)
    {
        var nextProjectPath = ActiveProjectPathSelectionPolicy.Select(
            activeProjectPath,
            observedCodexSnapshot,
            observedCliSnapshot);

        if (!string.IsNullOrWhiteSpace(nextProjectPath))
        {
            nextProjectPath = projectInspector.NormalizeProjectPath(nextProjectPath);
        }

        var changed = !string.Equals(activeProjectPath, nextProjectPath, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            Console.WriteLine($"Project switched: {lastLoggedProjectPath} -> {nextProjectPath}");
            lastLoggedProjectPath = nextProjectPath;
        }

        return (nextProjectPath, changed);
    }

    private CodexProcessSnapshot BuildCodexSnapshot(
        string activeProjectPath,
        ProjectSnapshot projectSnapshot,
        GitSnapshot gitSnapshot,
        ProfileRuntimeState selectedProfileState)
    {
        var codexSnapshot = selectedProfileState.Detector.GetSnapshot(
            activeProjectPath,
            projectSnapshot,
            gitSnapshot,
            selectedProfileState.LastActivityKind);
        var analyzingRepeatCount = ActivityRepeatCountTracker.GetAnalyzingRepeatCount(
            codexSnapshot.ActivityKind,
            selectedProfileState.LastActivityKind,
            codexSnapshot.LastTaskStartedAt,
            selectedProfileState.LastAnalyzingTaskStartedAt,
            selectedProfileState.LastAnalyzingRepeatCount);
        codexSnapshot = codexSnapshot with { ActivityRepeatCount = analyzingRepeatCount };
        return codexSnapshot with
        {
            ActivityStartedAt = ResolveActivityStartedAt(
                codexSnapshot.ActivityKind,
                selectedProfileState.LastActivityKind,
                selectedProfileState.LastActivityStartedAt,
                selectedProfileState.LastAnalyzingStartedAt,
                codexSnapshot.LastObservedAt,
                _options.Presence.RunningCommandHoldSeconds)
        };
    }

    private ModelNameSnapshot UpdateModelSnapshot(string activeProjectPath, ProfileRuntimeState selectedProfileState)
    {
        var modelSnapshot = selectedProfileState.ModelNameProvider.GetSnapshot(activeProjectPath);
        if (selectedProfileState.LastModelSnapshot is null ||
            !string.Equals(modelSnapshot.SelectedUiModel, selectedProfileState.LastModelSnapshot.SelectedUiModel, StringComparison.Ordinal) ||
            !string.Equals(modelSnapshot.LastUsedSessionModel, selectedProfileState.LastModelSnapshot.LastUsedSessionModel, StringComparison.Ordinal) ||
            !string.Equals(modelSnapshot.FinalDisplayedModel, selectedProfileState.LastModelSnapshot.FinalDisplayedModel, StringComparison.Ordinal))
        {
            Console.WriteLine(
                "Model detection: " +
                $"Selected UI model={FormatLogValue(modelSnapshot.SelectedUiModel)}, " +
                $"Last used session model={FormatLogValue(modelSnapshot.LastUsedSessionModel)}, " +
                $"Final displayed model={FormatLogValue(modelSnapshot.FinalDisplayedModel)} " +
                $"(source={modelSnapshot.Source})");
            selectedProfileState.LastModelSnapshot = modelSnapshot;
        }

        if (selectedProfileState.StableCostModelName is null &&
            !string.IsNullOrWhiteSpace(modelSnapshot.FinalDisplayedModel))
        {
            selectedProfileState.StableCostModelName = modelSnapshot.FinalDisplayedModel;
        }

        return modelSnapshot;
    }

    private PresenceContext BuildPresenceContext(
        SessionClock session,
        string activeProjectPath,
        ProfileRuntimeState selectedProfileState,
        ModelNameSnapshot modelSnapshot,
        ProjectSnapshot projectSnapshot,
        GitSnapshot gitSnapshot,
        CodexProcessSnapshot codexSnapshot)
    {
        return new PresenceContext(
            modelSnapshot.FinalDisplayedModel,
            codexSnapshot,
            projectSnapshot,
            gitSnapshot,
            session.GetSnapshot(),
            selectedProfileState.TokenUsageProvider.GetSnapshot(activeProjectPath, selectedProfileState.StableCostModelName));
    }

    private void UpdateDiscordPresence(
        DiscordPresenceClient rpc,
        TimeSpan keepAliveInterval,
        ProfileRuntimeState selectedProfileState,
        RenderedPresence presence)
    {
        var presenceSignature = BuildPresenceSignature(presence);
        var keepAliveDue = PresenceUpdatePolicy.ShouldSendKeepAlive(selectedProfileState.LastSuccessfulUpdateUtc, DateTime.UtcNow, keepAliveInterval);
        var shouldSendPresence = PresenceDispatchPolicy.ShouldSendPresence(
            presenceSignature,
            selectedProfileState.LastPresenceSignature,
            keepAliveDue,
            rpc.NeedsPresenceRefresh);

        if (!string.Equals(presence.Details, selectedProfileState.LastPresenceDetails, StringComparison.Ordinal) ||
            !string.Equals(presence.State, selectedProfileState.LastPresenceState, StringComparison.Ordinal))
        {
            Console.WriteLine(
                $"Presence rendered: Details={FormatLogValueForMultiline(presence.Details)}; " +
                $"State={FormatLogValueForMultiline(presence.State)}");
            selectedProfileState.LastPresenceDetails = presence.Details;
            selectedProfileState.LastPresenceState = presence.State;
        }

        if (shouldSendPresence && rpc.Update(presence))
        {
            selectedProfileState.LastPresenceSignature = presenceSignature;
            selectedProfileState.LastSuccessfulUpdateUtc = DateTime.UtcNow;
        }
    }

    private void UpdateProfileActivityState(ProfileRuntimeState selectedProfileState, CodexProcessSnapshot codexSnapshot)
    {
        if (selectedProfileState.LastActivitySnapshot is null ||
            selectedProfileState.LastActivitySnapshot.ActivityKind != codexSnapshot.ActivityKind ||
            selectedProfileState.LastActivitySnapshot.ActivityProvenance != codexSnapshot.ActivityProvenance ||
            !string.Equals(selectedProfileState.LastActivitySnapshot.ActivityReason, codexSnapshot.ActivityReason, StringComparison.Ordinal))
        {
            Console.WriteLine(
                "Activity detection: " +
                $"state={codexSnapshot.ActivityKind}, " +
                $"confidence={codexSnapshot.Confidence}, " +
                $"provenance={codexSnapshot.ActivityProvenance}, " +
                $"reason={codexSnapshot.ActivityReason}");
            selectedProfileState.LastActivitySnapshot = codexSnapshot;
        }

        selectedProfileState.LastAnalyzingRepeatCount = codexSnapshot.ActivityRepeatCount;
        selectedProfileState.LastAnalyzingTaskStartedAt = codexSnapshot.ActivityKind == CodexActivityKind.AnalyzingProject
            ? codexSnapshot.LastTaskStartedAt
            : null;
        if (codexSnapshot.ActivityKind == CodexActivityKind.AnalyzingProject)
        {
            selectedProfileState.LastAnalyzingStartedAt = codexSnapshot.ActivityStartedAt;
        }
        selectedProfileState.LastActivityStartedAt = codexSnapshot.ActivityStartedAt;
        selectedProfileState.LastActivityKind = codexSnapshot.ActivityKind;
    }

    private Dictionary<AppProfileKind, ProfileRuntimeState> BuildProfileStates()
    {
        return new Dictionary<AppProfileKind, ProfileRuntimeState>
        {
            [AppProfileKind.Codex] = new ProfileRuntimeState(
                AppProfileKind.Codex,
                _options.GetCodexDetectionOptions(AppProfileKind.Codex),
                _options.GetDiscordOptions(AppProfileKind.Codex),
                _options.Presence,
                _options.TokenUsage),
            [AppProfileKind.CodexCli] = new ProfileRuntimeState(
                AppProfileKind.CodexCli,
                _options.GetCodexDetectionOptions(AppProfileKind.CodexCli),
                _options.GetDiscordOptions(AppProfileKind.CodexCli),
                _options.Presence,
                _options.TokenUsage)
        };
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
        var cliLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(Path.Combine(_paths.BaseDirectory, SettingsFileNames.Cli));
        var userLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.UserSettingsPath);
        if (executableLastWriteTimeUtc == _executableSettingsLastWriteTimeUtc &&
            cliLastWriteTimeUtc == _cliSettingsLastWriteTimeUtc &&
            userLastWriteTimeUtc == _userSettingsLastWriteTimeUtc)
        {
            return;
        }

        _executableSettingsLastWriteTimeUtc = executableLastWriteTimeUtc;
        _cliSettingsLastWriteTimeUtc = cliLastWriteTimeUtc;
        _userSettingsLastWriteTimeUtc = userLastWriteTimeUtc;

        try
        {
            var reloadedOptions = AppOptions.LoadMerged(
                _paths.ExecutableSettingsPath,
                Path.Combine(_paths.BaseDirectory, SettingsFileNames.Cli),
                _paths.UserSettingsPath);
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
