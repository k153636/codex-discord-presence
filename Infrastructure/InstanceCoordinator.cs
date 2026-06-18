using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace CodexDiscordPresence;

public sealed class InstanceCoordinator : IDisposable
{
    private readonly string _pidFilePath;
    private readonly Mutex _mutex;

    private InstanceCoordinator(string pidFilePath, Mutex mutex)
    {
        _pidFilePath = pidFilePath;
        _mutex = mutex;
        File.WriteAllText(_pidFilePath, Environment.ProcessId.ToString());
    }

    public static InstanceCoordinator? TryAcquire(AppPaths paths)
    {
        var mutex = new Mutex(true, GetMutexName(paths), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new InstanceCoordinator(GetPidFilePath(paths), mutex);
    }

    public static int StopRunningInstance(AppPaths paths)
    {
        var pidFilePath = GetPidFilePath(paths);
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine(
                paths.Profile == AppProfileKind.CodexCli
                    ? "No running Codex CLI Discord RPC instance was found."
                    : "No running Codex Discord RPC instance was found.");
            return 0;
        }

        var pidText = File.ReadAllText(pidFilePath).Trim();
        if (!int.TryParse(pidText, out var pid))
        {
            File.Delete(pidFilePath);
            Console.WriteLine("Removed a stale PID file.");
            return 0;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            process.Kill(entireProcessTree: false);
            process.WaitForExit(5000);
            Console.WriteLine(
                paths.Profile == AppProfileKind.CodexCli
                    ? $"Stopped Codex CLI Discord RPC (PID {pid})."
                    : $"Stopped Codex Discord RPC (PID {pid}).");
        }
        catch (ArgumentException)
        {
            Console.WriteLine("The saved process was no longer running.");
        }
        finally
        {
            if (File.Exists(pidFilePath))
            {
                File.Delete(pidFilePath);
            }
        }

        return 0;
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_pidFilePath))
            {
                File.Delete(_pidFilePath);
            }
        }
        catch
        {
            // Ignore PID cleanup failures during shutdown.
        }

        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }

    private static string GetPidFilePath(AppPaths paths)
    {
        return Path.Combine(paths.AppDataDirectory, paths.Profile == AppProfileKind.CodexCli
            ? "codex-discord-presence-cli.pid"
            : "codex-discord-presence.pid");
    }

    private static string GetMutexName(AppPaths paths)
    {
        var normalized = Path.GetFullPath(paths.AppDataDirectory).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return paths.Profile == AppProfileKind.CodexCli
            ? @"Local\CodexDiscordPresenceCli_" + Convert.ToHexString(hash[..8])
            : @"Local\CodexDiscordPresence_" + Convert.ToHexString(hash[..8]);
    }
}
