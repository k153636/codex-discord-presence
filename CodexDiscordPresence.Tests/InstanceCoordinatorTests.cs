using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using CodexDiscordPresence;

namespace CodexDiscordPresence.Tests;

public sealed class InstanceCoordinatorTests
{
    [Fact]
    public void TryAcquire_WritesPidAbsoluteExecutableAndStartIdentity()
    {
        using var fixture = new InstanceFixture();
        using var instance = InstanceCoordinator.TryAcquire(fixture.Paths);
        using var process = Process.GetCurrentProcess();

        Assert.NotNull(instance);
        var identity = InstanceProcessIdentity.Parse(File.ReadAllText(fixture.RecordPath));
        Assert.NotNull(identity);
        Assert.Equal(Environment.ProcessId, identity.ProcessId);
        Assert.True(InstanceProcessIdentity.PathsMatch(Environment.ProcessPath, identity.ExecutablePath));
        Assert.Equal(process.StartTime.ToUniversalTime().Ticks, identity.StartedAtUtcTicks);
        Assert.Null(InstanceCoordinator.TryAcquire(fixture.Paths));
    }

    [Fact]
    public void TryAcquire_RecordWriteFailure_ReleasesOwnedMutex()
    {
        using var fixture = new InstanceFixture();
        var missingDirectory = Path.Combine(fixture.Paths.AppDataDirectory, "not-created");
        var paths = fixture.Paths with { AppDataDirectory = missingDirectory };

        Assert.Throws<DirectoryNotFoundException>(() => InstanceCoordinator.TryAcquire(paths));
        Directory.CreateDirectory(missingDirectory);
        using var instance = InstanceCoordinator.TryAcquire(paths);
        Assert.NotNull(instance);
    }

    [Fact]
    public void Dispose_PreservesReplacementRecordAndReleasesMutex()
    {
        using var fixture = new InstanceFixture();
        var instance = InstanceCoordinator.TryAcquire(fixture.Paths);
        Assert.NotNull(instance);
        fixture.WriteIdentity(fixture.Identity);
        var replacement = File.ReadAllText(fixture.RecordPath);

        instance.Dispose();
        instance.Dispose();

        Assert.Equal(replacement, File.ReadAllText(fixture.RecordPath));
        using var nextInstance = InstanceCoordinator.TryAcquire(fixture.Paths);
        Assert.NotNull(nextInstance);
    }

    [Fact]
    public void Dispose_RecordCleanupDenied_StillReleasesMutex()
    {
        using var fixture = new InstanceFixture();
        var instance = InstanceCoordinator.TryAcquire(fixture.Paths);
        Assert.NotNull(instance);

        using (File.Open(fixture.RecordPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            instance.Dispose();
        }

        using var nextInstance = InstanceCoordinator.TryAcquire(fixture.Paths);
        Assert.NotNull(nextInstance);
    }

    [Fact]
    public void TryAcquire_RecordReplacementDenied_PreservesRecordAndReleasesMutex()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);

        using (File.Open(fixture.RecordPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = Record.Exception(() => InstanceCoordinator.TryAcquire(fixture.Paths));
            Assert.True(error is IOException or UnauthorizedAccessException);
        }

        Assert.Equal(fixture.Identity, InstanceProcessIdentity.Parse(File.ReadAllText(fixture.RecordPath)));
        Assert.Empty(Directory.GetFiles(fixture.Paths.AppDataDirectory, "*.tmp"));
        using var nextInstance = InstanceCoordinator.TryAcquire(fixture.Paths);
        Assert.NotNull(nextInstance);
    }

    [Fact]
    public void StopRunningInstance_NoRecord_DoesNotInspectAnyProcess()
    {
        using var fixture = new InstanceFixture();

        Assert.Equal(0, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
    }

    [Fact]
    public void StopRunningInstance_VerifiedIdentity_StopsOnlyOpenedProcess()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        using var process = new FakeInstanceProcess(fixture.Identity);

        var result = fixture.Stop(pid =>
        {
            Assert.Equal(fixture.Identity.ProcessId, pid);
            return process;
        });

        Assert.Equal(0, result);
        Assert.True(process.Killed);
        Assert.Equal(5000, process.WaitTimeout);
        Assert.True(process.Disposed);
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Theory]
    [InlineData("pid")]
    [InlineData("path")]
    [InlineData("start")]
    public void StopRunningInstance_IdentityMismatch_DoesNotKillProcess(string mismatch)
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        var actualIdentity = mismatch switch
        {
            "pid" => fixture.Identity with { ProcessId = fixture.Identity.ProcessId + 1 },
            "path" => fixture.Identity with { ExecutablePath = Path.Combine(fixture.Paths.BaseDirectory, "other", "discord-presence-for-codex.exe") },
            _ => fixture.Identity with { StartedAtUtcTicks = fixture.Identity.StartedAtUtcTicks + 1 }
        };
        using var process = new FakeInstanceProcess(actualIdentity);

        Assert.Equal(0, fixture.Stop(_ => process));
        Assert.False(process.Killed);
        Assert.Null(process.WaitTimeout);
        Assert.True(process.Disposed);
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_SavedPathDoesNotMatchApplication_RefusesAndRetainsRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity with { ExecutablePath = Path.Combine(fixture.Paths.BaseDirectory, "other", "discord-presence-for-codex.exe") });
        var saved = File.ReadAllText(fixture.RecordPath);

        Assert.Equal(1, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
        Assert.Equal(saved, File.ReadAllText(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_SavedPidIsCurrentProcess_RefusesWithoutProcessInspection()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity with { ProcessId = Environment.ProcessId });

        Assert.Equal(1, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect itself.")));
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_PublicEntryPoint_NeverStopsItsOwnProcess()
    {
        using var fixture = new InstanceFixture();
        using var instance = InstanceCoordinator.TryAcquire(fixture.Paths);

        Assert.Equal(1, InstanceCoordinator.StopRunningInstance(fixture.Paths));
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_ExecutableIdentityUnavailable_RefusesAndRetainsRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);

        Assert.Equal(1, InstanceCoordinator.StopRunningInstance(fixture.Paths, null,
            _ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_ProcessAlreadyExited_RemovesStaleRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);

        Assert.Equal(0, fixture.Stop(_ => throw new ArgumentException("Process no longer exists.")));
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Theory]
    [InlineData("invalid json")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("2147483648")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"ProcessId\":24680,\"ExecutablePath\":\"relative.exe\",\"StartedAtUtcTicks\":123}")]
    [InlineData("{\"ProcessId\":24680,\"ExecutablePath\":null,\"StartedAtUtcTicks\":123}")]
    [InlineData("{\"ProcessId\":24680,\"ExecutablePath\":\"C:\\\\app.exe\",\"StartedAtUtcTicks\":0}")]
    [InlineData("{\"ProcessId\":24680,\"ExecutablePath\":\"C:\\\\app.exe\",\"StartedAtUtcTicks\":3155378976000000000}")]
    public void StopRunningInstance_InvalidRecord_DoesNotInspectAnyProcess(string text)
    {
        using var fixture = new InstanceFixture();
        File.WriteAllText(fixture.RecordPath, text);

        Assert.Equal(0, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopRunningInstance_LegacyRecordWithLivePid_RefusesRegardlessOfExecutable(bool matchesExecutable)
    {
        using var fixture = new InstanceFixture();
        File.WriteAllText(fixture.RecordPath, fixture.Identity.ProcessId.ToString());
        var identity = matchesExecutable ? fixture.Identity : fixture.Identity with
        {
            ExecutablePath = Path.Combine(fixture.Paths.BaseDirectory, "unrelated.exe")
        };
        using var process = new FakeInstanceProcess(identity);

        Assert.Equal(1, fixture.Stop(_ => process));
        Assert.False(process.Killed);
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_LegacyRecordWithExitedPid_RemovesStaleRecord()
    {
        using var fixture = new InstanceFixture();
        File.WriteAllText(fixture.RecordPath, fixture.Identity.ProcessId.ToString());

        Assert.Equal(0, fixture.Stop(_ => throw new ArgumentException("Process no longer exists.")));
        Assert.False(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_MetadataAccessDenied_RefusesAndRetainsRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);

        Assert.Equal(1, fixture.Stop(_ => throw new Win32Exception(5)));
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_RecordReadDenied_RefusesWithoutProcessInspection()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        using var lockedFile = File.Open(fixture.RecordPath, FileMode.Open, FileAccess.Read, FileShare.None);

        Assert.Equal(1, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_VerifiedProcessDoesNotExit_ReportsFailureAndRetainsRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        using var process = new FakeInstanceProcess(fixture.Identity) { Exited = false };

        Assert.Equal(1, fixture.Stop(_ => process));
        Assert.True(process.Killed);
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_TerminationDenied_ReportsFailureAndRetainsRecord()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        using var process = new FakeInstanceProcess(fixture.Identity) { OnKill = () => throw new Win32Exception(5) };

        Assert.Equal(1, fixture.Stop(_ => process));
        Assert.True(process.Disposed);
        Assert.True(File.Exists(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_RecordReplacedDuringTermination_PreservesNewIdentity()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity);
        var replacement = fixture.Identity with { StartedAtUtcTicks = fixture.Identity.StartedAtUtcTicks + 1 };
        using var process = new FakeInstanceProcess(fixture.Identity) { OnKill = () => fixture.WriteIdentity(replacement) };

        Assert.Equal(0, fixture.Stop(_ => process));
        Assert.Equal(replacement, InstanceProcessIdentity.Parse(File.ReadAllText(fixture.RecordPath)));
    }

    [Fact]
    public void StopRunningInstance_ApplicationOwnsMutex_PreservesInvalidRecord()
    {
        using var fixture = new InstanceFixture();
        using var instance = InstanceCoordinator.TryAcquire(fixture.Paths);
        File.WriteAllText(fixture.RecordPath, "invalid");

        Assert.Equal(0, fixture.Stop(_ => throw new Xunit.Sdk.XunitException("Must not inspect a process.")));
        Assert.Equal("invalid", File.ReadAllText(fixture.RecordPath));
    }

    [Fact]
    public void StopRunningInstance_CaseAndNormalizedPathMatch_StopsVerifiedProcess()
    {
        using var fixture = new InstanceFixture();
        fixture.WriteIdentity(fixture.Identity with
        {
            ExecutablePath = Path.Combine(fixture.Paths.BaseDirectory, ".", "discord-presence-for-codex.exe").ToUpperInvariant()
        });
        using var process = new FakeInstanceProcess(fixture.Identity);

        Assert.Equal(0, fixture.Stop(_ => process));
        Assert.True(process.Killed);
    }

    private sealed class InstanceFixture : IDisposable
    {
        internal InstanceFixture()
        {
            var directory = Path.Combine(Path.GetTempPath(), "CodexInstanceTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Paths = AppPaths.Create(AppProfileKind.Codex, directory) with { AppDataDirectory = directory };
            Identity = new InstanceProcessIdentity(24680, Path.Combine(directory, "discord-presence-for-codex.exe"), DateTime.UtcNow.Ticks);
        }

        internal AppPaths Paths { get; }
        internal InstanceProcessIdentity Identity { get; }
        internal string RecordPath => Path.Combine(Paths.AppDataDirectory, "codex-discord-presence.pid");
        internal void WriteIdentity(InstanceProcessIdentity identity) => File.WriteAllText(RecordPath, JsonSerializer.Serialize(identity));
        internal int Stop(Func<int, IInstanceProcess> openProcess) =>
            InstanceCoordinator.StopRunningInstance(Paths, Identity.ExecutablePath, openProcess);
        public void Dispose() => Directory.Delete(Paths.AppDataDirectory, recursive: true);
    }

    private sealed class FakeInstanceProcess(InstanceProcessIdentity identity) : IInstanceProcess
    {
        public InstanceProcessIdentity Identity { get; } = identity;
        internal bool Killed { get; private set; }
        internal bool Disposed { get; private set; }
        internal bool Exited { get; init; } = true;
        internal int? WaitTimeout { get; private set; }
        internal Action? OnKill { get; init; }

        public void Kill()
        {
            OnKill?.Invoke();
            Killed = true;
        }

        public bool WaitForExit(int milliseconds)
        {
            WaitTimeout = milliseconds;
            return Exited;
        }

        public void Dispose() => Disposed = true;
    }
}
