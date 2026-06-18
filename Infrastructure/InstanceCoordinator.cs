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

    public static InstanceCoordinator? TryAcquire(string baseDirectory)
    {
        var mutex = new Mutex(true, GetMutexName(baseDirectory), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        return new InstanceCoordinator(GetPidFilePath(baseDirectory), mutex);
    }

    public static int StopRunningInstance(string baseDirectory)
    {
        var pidFilePath = GetPidFilePath(baseDirectory);
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine("No running Codex Discord RPC instance was found.");
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
            Console.WriteLine($"Stopped Codex Discord RPC (PID {pid}).");
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

    private static string GetPidFilePath(string baseDirectory)
    {
        return Path.Combine(baseDirectory, "codex-discord-presence.pid");
    }

    private static string GetMutexName(string baseDirectory)
    {
        var normalized = Path.GetFullPath(baseDirectory).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return @"Local\CodexDiscordPresence_" + Convert.ToHexString(hash[..8]);
    }
}
