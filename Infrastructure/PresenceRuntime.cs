namespace CodexDiscordPresence;

public sealed class PresenceRuntime
{
    private readonly AppOptions _options;
    private readonly PresenceRuntimeState _state;
    private readonly CancellationToken _cancellationToken;
    private readonly AppPaths _paths;
    private readonly DiagnosticLog _log;
    private readonly ForegroundProjectPathDetector _foregroundProjectPathDetector;
    private RuntimeTimingSettings _timingSettings;
    private DateTime _executableSettingsLastWriteTimeUtc;
    private DateTime _cliSettingsLastWriteTimeUtc;
    private DateTime _userSettingsLastWriteTimeUtc;
    private string? _lastLoggedFocusedProjectPath;
    private string? _lastLoggedFocusedProjectPathDecision;

    public PresenceRuntime(AppOptions options, PresenceRuntimeState state, CancellationToken cancellationToken, AppPaths paths, DiagnosticLog log)
    {
        _options = options;
        _state = state;
        _cancellationToken = cancellationToken;
        _paths = paths;
        _log = log;
        _foregroundProjectPathDetector = new ForegroundProjectPathDetector();
        _timingSettings = RuntimeTimingSettings.From(options);
        _executableSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.ExecutableSettingsPath);
        _cliSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(Path.Combine(_paths.BaseDirectory, SettingsFileNames.Cli));
        _userSettingsLastWriteTimeUtc = GetSettingsLastWriteTimeUtc(_paths.UserSettingsPath);
    }

    public async Task RunAsync()
    {
        var session = new SessionClock(DateTime.UtcNow);
        var profileStates = BuildProfileStates();
        var projectInspector = new ProjectInspector(_options.Project);
        var gitInspector = new GitInspector();
        var renderer = new PresenceTemplateRenderer();
        var projectSwitchDetectionInterval = TimeSpan.FromSeconds(3);

        _log.Info("Starting Codex Discord RPC with auto-detection.");
        var activeProjectPath = projectInspector.ProjectPath;
        _log.Info($"Project path: {activeProjectPath}");
        _log.Info("Press Ctrl+C or Quit to stop.");

        var initialCodexProbe = profileStates[AppProfileKind.Codex].Detector.GetSnapshot(activeProjectPath);
        var initialCliProbe = profileStates[AppProfileKind.CodexCli].Detector.GetSnapshot(activeProjectPath);
        var currentProfile = AppProfileSelectionPolicy.Select(
            AppProfileKind.Codex,
            new AppProfileSelectionCandidate(AppProfileKind.Codex, initialCodexProbe, profileStates[AppProfileKind.Codex].DiscordOptions),
            new AppProfileSelectionCandidate(AppProfileKind.CodexCli, initialCliProbe, profileStates[AppProfileKind.CodexCli].DiscordOptions));
        var rpc = new DiscordPresenceClient(profileStates[currentProfile].DiscordOptions, _log);

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
                    _log.Info("Presence enabled.");
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
                    _foregroundProjectPathDetector.GetFocusedProjectPath(),
                    _log,
                    ref lastLoggedProjectPath);
                activeProjectPath = nextProjectPath;

                if (projectPathChanged)
                {
                    ResetAllProfilePresenceCaches(profileStates);
                }

                var selectedProfile = SelectProfile(profileStates, activeProjectPath, currentProfile);
                var selectedProfileState = profileStates[selectedProfile];
                var selectedProfileProjectPath = ResolveProfileProjectPath(selectedProfileState, activeProjectPath);

                rpc.UpdateOptions(selectedProfileState.DiscordOptions);

                if (selectedProfile != currentProfile)
                {
                    _log.Info($"Profile switched: {currentProfile} -> {selectedProfile}");
                    currentProfile = selectedProfile;
                }

                var projectSnapshot = projectInspector.GetSnapshot(selectedProfileProjectPath);
                var gitSnapshot = gitInspector.GetSnapshot(selectedProfileProjectPath);
                var codexSnapshot = BuildCodexSnapshot(
                    selectedProfileProjectPath,
                    projectSnapshot,
                    gitSnapshot,
                    selectedProfileState);
                var modelSnapshot = UpdateModelSnapshot(selectedProfileProjectPath, selectedProfileState);
                var context = BuildPresenceContext(
                    session,
                    selectedProfileProjectPath,
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
                _log.Error("Presence update loop failed", ex);
            }

            var delay = PresenceRefreshPolicy.GetNextDelay(_options.Presence, profileStates[currentProfile].LastActivityKind, _options.UpdateIntervalSeconds);
            if (delay > projectSwitchDetectionInterval)
            {
                delay = projectSwitchDetectionInterval;
            }

            await Delay(delay);
        }

        rpc.Clear();
        _log.Info("Stopped Codex Discord RPC.");
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
            _log.Info("Presence disabled.");
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
        var codexProfileProjectPath = ResolveProfileProjectPath(profileStates[AppProfileKind.Codex], activeProjectPath);
        var cliProfileProjectPath = ResolveProfileProjectPath(profileStates[AppProfileKind.CodexCli], activeProjectPath);
        var codexProfileSnapshot = profileStates[AppProfileKind.Codex].Detector.GetSnapshot(codexProfileProjectPath);
        var cliProfileSnapshot = profileStates[AppProfileKind.CodexCli].Detector.GetSnapshot(cliProfileProjectPath);
        return AppProfileSelectionPolicy.Select(
            currentProfile,
            new AppProfileSelectionCandidate(AppProfileKind.Codex, codexProfileSnapshot, profileStates[AppProfileKind.Codex].DiscordOptions),
            new AppProfileSelectionCandidate(AppProfileKind.CodexCli, cliProfileSnapshot, profileStates[AppProfileKind.CodexCli].DiscordOptions));
    }

    private static string ResolveProfileProjectPath(ProfileRuntimeState profileState, string activeProjectPath)
    {
        return profileState.Detector.GetObservedProjectPath() ?? activeProjectPath;
    }

    private (string ActiveProjectPath, bool Changed) UpdateActiveProjectPath(
        ProjectInspector projectInspector,
        string activeProjectPath,
        CodexProcessSnapshot observedCodexSnapshot,
        CodexProcessSnapshot observedCliSnapshot,
        string? focusedProjectPath,
        DiagnosticLog log,
        ref string lastLoggedProjectPath)
    {
        if (!string.IsNullOrWhiteSpace(focusedProjectPath))
        {
            if (ActiveProjectPathSelectionPolicy.TryNormalizeFocusedProjectPath(
                    focusedProjectPath,
                    out var normalizedFocusedProjectPath,
                    out var rejectionReason))
            {
                var decision = $"accepted; normalized={normalizedFocusedProjectPath}";
                if (!string.Equals(_lastLoggedFocusedProjectPath, focusedProjectPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_lastLoggedFocusedProjectPathDecision, decision, StringComparison.Ordinal))
                {
                    log.Info(
                        "Focused project path accepted: " +
                        $"raw={focusedProjectPath}; normalized={normalizedFocusedProjectPath}");
                    _lastLoggedFocusedProjectPath = focusedProjectPath;
                    _lastLoggedFocusedProjectPathDecision = decision;
                }
            }
            else
            {
                var decision = $"rejected; reason={rejectionReason}";
                if (!string.Equals(_lastLoggedFocusedProjectPath, focusedProjectPath, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(_lastLoggedFocusedProjectPathDecision, decision, StringComparison.Ordinal))
                {
                    log.Info(
                        "Focused project path rejected: " +
                        $"raw={focusedProjectPath}; reason={rejectionReason}");
                    _lastLoggedFocusedProjectPath = focusedProjectPath;
                    _lastLoggedFocusedProjectPathDecision = decision;
                }
            }
        }

        var nextProjectPath = ActiveProjectPathSelectionPolicy.Select(
            activeProjectPath,
            focusedProjectPath,
            observedCodexSnapshot,
            observedCliSnapshot);

        if (!string.IsNullOrWhiteSpace(nextProjectPath))
        {
            nextProjectPath = projectInspector.NormalizeProjectPath(nextProjectPath);
        }

        var changed = !string.Equals(activeProjectPath, nextProjectPath, StringComparison.OrdinalIgnoreCase);
        if (changed)
        {
            log.Info($"Project switched: {lastLoggedProjectPath} -> {nextProjectPath}");
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
            !string.Equals(modelSnapshot.FinalDisplayedModel, selectedProfileState.LastModelSnapshot.FinalDisplayedModel, StringComparison.Ordinal) ||
            !string.Equals(modelSnapshot.ReasoningEffort, selectedProfileState.LastModelSnapshot.ReasoningEffort, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(modelSnapshot.ServiceTier, selectedProfileState.LastModelSnapshot.ServiceTier, StringComparison.OrdinalIgnoreCase))
        {
            _log.Info(
                "Model detection: " +
                $"Selected UI model={FormatLogValue(modelSnapshot.SelectedUiModel)}, " +
                $"Last used session model={FormatLogValue(modelSnapshot.LastUsedSessionModel)}, " +
                $"Reasoning effort={FormatLogValue(modelSnapshot.ReasoningEffort)}, " +
                $"Effective service tier={FormatLogValue(modelSnapshot.ServiceTier)}, " +
                $"Final displayed model={FormatLogValue(modelSnapshot.DisplayLabel)} " +
                $"(raw model={FormatLogValue(modelSnapshot.FinalDisplayedModel)}, " +
                $"source={modelSnapshot.Source})");
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
            modelSnapshot.DisplayLabel,
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
            _log.Info(
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
        var previousSnapshot = selectedProfileState.LastActivitySnapshot;

        if (previousSnapshot is null ||
            previousSnapshot.ActivityKind != codexSnapshot.ActivityKind ||
            previousSnapshot.ActivityProvenance != codexSnapshot.ActivityProvenance ||
            !string.Equals(previousSnapshot.ActivityReason, codexSnapshot.ActivityReason, StringComparison.Ordinal))
        {
            var recentEditedFiles = codexSnapshot.RecentEditedFiles
                .Take(3)
                .Select(file => file.Name)
                .ToArray();
            var recentEditedFilesText = recentEditedFiles.Length == 0
                ? "<none>"
                : string.Join(", ", recentEditedFiles);
            _log.Info(
                "Activity detection: " +
                $"state={codexSnapshot.ActivityKind}, " +
                $"confidence={codexSnapshot.Confidence}, " +
                $"provenance={codexSnapshot.ActivityProvenance}, " +
                $"reason={codexSnapshot.ActivityReason}, " +
                $"recentEditedFiles={recentEditedFilesText}, " +
                $"recentEditedFileCount={codexSnapshot.RecentEditedFiles.Count}, " +
                $"runningCommandKind={codexSnapshot.RunningCommandKind}, " +
                $"runningCommandName={FormatLogValue(codexSnapshot.RunningCommandName)}, " +
                $"investigative={codexSnapshot.LastShellCommandWasInvestigative}, " +
                $"lastTaskStartedAt={FormatTimestamp(codexSnapshot.LastTaskStartedAt)}, " +
                $"lastShellCommandAt={FormatTimestamp(codexSnapshot.LastShellCommandAt)}, " +
                $"lastObservedAt={FormatTimestamp(codexSnapshot.LastObservedAt)}");
            selectedProfileState.LastActivitySnapshot = codexSnapshot;
        }

        if (previousSnapshot is not null &&
            previousSnapshot.ActivityKind != codexSnapshot.ActivityKind)
        {
            _log.Info(BuildActivityTransitionLog(previousSnapshot, codexSnapshot));
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

    private static string FormatTimestamp(DateTime? value)
    {
        return value.HasValue
            ? value.Value.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            : "<none>";
    }

    private static string BuildActivityTransitionLog(CodexProcessSnapshot previousSnapshot, CodexProcessSnapshot currentSnapshot)
    {
        var transitionStart = previousSnapshot.LastObservedAt
            ?? previousSnapshot.ActivityStartedAt
            ?? currentSnapshot.LastObservedAt
            ?? currentSnapshot.ActivityStartedAt;
        var transitionEnd = currentSnapshot.LastObservedAt
            ?? currentSnapshot.ActivityStartedAt
            ?? DateTime.UtcNow;
        if (transitionStart.HasValue && transitionEnd < transitionStart.Value)
        {
            transitionEnd = transitionStart.Value;
        }

        var duration = transitionStart.HasValue
            ? FormatDuration(transitionEnd - transitionStart.Value)
            : "<unknown>";

        return
            "Activity transition: " +
            $"{previousSnapshot.ActivityKind} -> {currentSnapshot.ActivityKind}; " +
            $"startedAt={FormatTimestamp(transitionStart)}; " +
            $"endedAt={FormatTimestamp(transitionEnd)}; " +
            $"duration={duration}; " +
            $"fromReason={FormatLogValueForMultiline(previousSnapshot.ActivityReason)}; " +
            $"toReason={FormatLogValueForMultiline(currentSnapshot.ActivityReason)}";
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalDays >= 1)
        {
            return $"{(int)duration.TotalDays}d {duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
        }

        if (duration.TotalHours >= 1)
        {
            return $"{(int)duration.TotalHours}h {duration.Minutes}m {duration.Seconds}s";
        }

        if (duration.TotalMinutes >= 1)
        {
            return $"{duration.Minutes}m {duration.Seconds}s";
        }

        return $"{duration.Seconds}s";
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
            presence.ActivityKind.ToString(),
            presence.RunningCommandKind.ToString(),
            presence.RunningCommandName,
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
            return currentObservedAt ?? lastActivityStartedAt ?? DateTime.UtcNow;
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
                _log.Info(
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
            _log.Error("Failed to reload timing settings", ex);
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
