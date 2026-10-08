namespace CodexDiscordPresence.Tests;

public sealed class ApplicationUpdateCoordinatorTests
{
    private static readonly DateTime Now = new(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task AutomaticUpdate_DownloadsButWaitsForUninterruptedIdleMinute()
    {
        using var fixture = new Fixture();
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(1, fixture.Backend.Downloads);
        Assert.Equal(ApplicationUpdateStatus.Ready, fixture.Updates.Snapshot.Status);
        Assert.Null(fixture.Updates.Snapshot.RestartAtUtc);
        fixture.Idle = true;
        await fixture.Updates.TickAsync(Now.AddSeconds(5), default);
        Assert.Equal(Now.AddSeconds(65), fixture.Updates.Snapshot.RestartAtUtc);
        fixture.Idle = false;
        await fixture.Updates.TickAsync(Now.AddSeconds(60), default);
        fixture.Idle = true;
        await fixture.Updates.TickAsync(Now.AddSeconds(65), default);
        await fixture.Updates.TickAsync(Now.AddSeconds(124), default);
        Assert.False(fixture.Exited);
        await fixture.Updates.TickAsync(Now.AddSeconds(125), default);
        Assert.True(fixture.Exited);
        Assert.Equal(["backup", "schedule", "exit"], fixture.Events);
        Assert.NotNull(fixture.Backend.Arguments);
        Assert.Equal(["--project", "E:\\a project"], fixture.Backend.Arguments);
    }

    [Fact]
    public async Task Defer_PersistsAndClearsCountdownUntilTheNextDay()
    {
        using var fixture = new Fixture { Idle = true };
        await fixture.Updates.TickAsync(Now, default);
        fixture.Updates.DeferUntil(Now.AddDays(1));
        Assert.Null(fixture.Updates.Snapshot.RestartAtUtc);
        await fixture.Updates.TickAsync(Now.AddMinutes(2), default);
        Assert.False(fixture.Exited);
        Assert.Equal(Now.AddDays(1), fixture.Store.Load().DeferredUntilUtc);
        await fixture.Updates.TickAsync(Now.AddDays(1), default);
        Assert.Equal(Now.AddDays(1).AddMinutes(1), fixture.Updates.Snapshot.RestartAtUtc);
    }

    [Fact]
    public async Task AutomaticOff_ChecksButDownloadsOnlyAfterManualRequest()
    {
        using var fixture = new Fixture();
        fixture.Updates.SetAutomaticEnabled(false);
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(ApplicationUpdateStatus.Available, fixture.Updates.Snapshot.Status);
        Assert.Equal(0, fixture.Backend.Downloads);
        Assert.False(fixture.Store.Load().AutomaticEnabled);
        fixture.Updates.DeferUntil(Now.AddDays(1));
        fixture.Updates.RequestRestart();
        await fixture.Updates.TickAsync(Now.AddSeconds(1), default);
        Assert.True(fixture.Exited);
        Assert.Equal(1, fixture.Backend.Downloads);
    }

    [Fact]
    public async Task LooseExecutable_NeverDownloadsOrRestarts()
    {
        using var fixture = new Fixture(installed: false) { Idle = true };
        await fixture.Updates.TickAsync(Now, default);
        fixture.Updates.RequestRestart();
        await fixture.Updates.TickAsync(Now.AddMinutes(2), default);
        Assert.False(fixture.Updates.Snapshot.CanInstall);
        Assert.Equal(0, fixture.Backend.Downloads);
        Assert.False(fixture.Exited);
    }

    [Fact]
    public async Task FailedDownload_LeavesAppRunningAndRetriesAfterFifteenMinutes()
    {
        using var fixture = new Fixture();
        fixture.Backend.DownloadError = new InvalidDataException("checksum mismatch");
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(ApplicationUpdateStatus.Failed, fixture.Updates.Snapshot.Status);
        Assert.Contains("checksum", fixture.Updates.Snapshot.Error);
        await fixture.Updates.TickAsync(Now.AddMinutes(14), default);
        Assert.Equal(1, fixture.Backend.Downloads);
        Assert.False(fixture.Exited);
        fixture.Backend.DownloadError = null;
        await fixture.Updates.TickAsync(Now.AddMinutes(15), default);
        Assert.Equal(2, fixture.Backend.Downloads);
        Assert.Equal(ApplicationUpdateStatus.Ready, fixture.Updates.Snapshot.Status);
    }

    [Fact]
    public async Task FailedBackup_DoesNotScheduleReplacementOrExit()
    {
        using var fixture = new Fixture();
        fixture.BackupError = new IOException("settings are locked");
        fixture.Updates.RequestRestart();
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(ApplicationUpdateStatus.Failed, fixture.Updates.Snapshot.Status);
        Assert.Empty(fixture.Events);
        Assert.False(fixture.Exited);
    }

    [Fact]
    public async Task BackgroundChecksDisabled_StillPermitsAnExplicitManualUpdate()
    {
        using var fixture = new Fixture(checksEnabled: false);
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(0, fixture.Backend.Checks);
        fixture.Updates.RequestCheck();
        await fixture.Updates.TickAsync(Now.AddSeconds(1), default);
        Assert.Equal(1, fixture.Backend.Checks);
        Assert.Equal(0, fixture.Backend.Downloads);
        fixture.Updates.RequestRestart();
        await fixture.Updates.TickAsync(Now.AddSeconds(2), default);
        Assert.True(fixture.Exited);
    }

    [Theory]
    [InlineData("0.2.4")]
    [InlineData("0.2.5")]
    [InlineData("0.5.0-preview.1")]
    [InlineData("invalid")]
    public async Task OlderOrPrereleaseVersion_IsNeverApplied(string version)
    {
        using var fixture = new Fixture();
        fixture.Backend.Latest = version;
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal(ApplicationUpdateStatus.UpToDate, fixture.Updates.Snapshot.Status);
        Assert.Equal(0, fixture.Backend.Downloads);
    }

    [Fact]
    public async Task PreparedUpdate_SurvivesAppRestartWithoutDownloadingAgain()
    {
        using var fixture = new Fixture(prepared: "0.5.0") { Idle = true };
        await fixture.Updates.TickAsync(Now, default);
        Assert.Equal("0.5.0", fixture.Updates.Snapshot.AvailableVersion);
        Assert.Equal(0, fixture.Backend.Downloads);
        await fixture.Updates.TickAsync(Now.AddMinutes(1), default);
        Assert.True(fixture.Exited);
    }

    [Fact]
    public async Task Cancellation_DoesNotConvertQuitIntoFailureOrScheduleRestart()
    {
        using var fixture = new Fixture();
        using var cancellation = new CancellationTokenSource();
        fixture.Backend.DuringDownload = () => cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Updates.TickAsync(Now, cancellation.Token));
        Assert.False(fixture.Exited);
        Assert.NotEqual(ApplicationUpdateStatus.Failed, fixture.Updates.Snapshot.Status);
    }

    [Fact]
    public async Task AutomaticOffDuringDownload_LeavesPreparedUpdateForManualUse()
    {
        using var fixture = new Fixture { Idle = true };
        fixture.Backend.DuringDownload = () => fixture.Updates.SetAutomaticEnabled(false);
        await fixture.Updates.TickAsync(Now, default);
        await fixture.Updates.TickAsync(Now.AddMinutes(2), default);
        Assert.Equal(ApplicationUpdateStatus.Ready, fixture.Updates.Snapshot.Status);
        Assert.Null(fixture.Updates.Snapshot.RestartAtUtc);
        Assert.False(fixture.Exited);
    }

    [Fact]
    public async Task UpToDate_ChecksOnlyEverySixHours()
    {
        using var fixture = new Fixture();
        fixture.Backend.Latest = null;
        await fixture.Updates.TickAsync(Now, default);
        await fixture.Updates.TickAsync(Now.AddHours(5), default);
        Assert.Equal(1, fixture.Backend.Checks);
        await fixture.Updates.TickAsync(Now.AddHours(6), default);
        Assert.Equal(2, fixture.Backend.Checks);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "rpc-update-tests-" + Guid.NewGuid());
        public FakeBackend Backend { get; }
        public UpdatePreferencesStore Store { get; }
        public ApplicationUpdateCoordinator Updates { get; }
        public bool Idle { get; set; }
        public bool Exited { get; private set; }
        public Exception? BackupError { get; set; }
        public List<string> Events { get; } = [];

        public Fixture(bool installed = true, bool checksEnabled = true, string? prepared = null)
        {
            Backend = new FakeBackend(Events) { IsInstalled = installed, PreparedVersion = prepared };
            Store = new UpdatePreferencesStore(Path.Combine(_directory, "preferences.json"));
            Updates = new ApplicationUpdateCoordinator(Backend, Store, () => Idle,
                () => { if (BackupError is not null) throw BackupError; Events.Add("backup"); },
                () => ["--project", "E:\\a project"], () => { Events.Add("exit"); Exited = true; },
                _ => { }, checksEnabled, new SemanticVersion(0, 2, 5, null));
        }

        public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
    }

    private sealed class FakeBackend(List<string> events) : IApplicationUpdateBackend
    {
        public bool IsInstalled { get; set; }
        public string? Latest { get; set; } = "0.5.0";
        public string? PreparedVersion { get; set; }
        public int Checks { get; private set; }
        public int Downloads { get; private set; }
        public string[]? Arguments { get; private set; }
        public Exception? DownloadError { get; set; }
        public Action? DuringDownload { get; set; }
        public Task<string?> CheckAsync(CancellationToken token) { Checks++; return Task.FromResult(Latest); }
        public Task DownloadAsync(Action<int> progress, CancellationToken token)
        {
            Downloads++;
            DuringDownload?.Invoke();
            token.ThrowIfCancellationRequested();
            if (DownloadError is not null) throw DownloadError;
            progress(100);
            PreparedVersion = Latest;
            return Task.CompletedTask;
        }
        public void ScheduleRestart(string[] arguments) { events.Add("schedule"); Arguments = arguments; }
    }
}
