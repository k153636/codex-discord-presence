using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

public sealed class InstanceCoordinator : IDisposable
{
    private readonly string _pidFilePath;
    private readonly string _savedIdentity;
    private readonly Mutex _mutex;
    private bool _disposed;

    private InstanceCoordinator(string pidFilePath, Mutex mutex)
    {
        _pidFilePath = pidFilePath;
        _mutex = mutex;
        using var process = VerifiedInstanceProcess.Open(Environment.ProcessId);
        _savedIdentity = JsonSerializer.Serialize(process.Identity);
        var temporaryPath = _pidFilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporaryPath, _savedIdentity);
            File.Move(temporaryPath, _pidFilePath, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    public static InstanceCoordinator? TryAcquire(AppPaths paths)
    {
        var mutex = new Mutex(true, GetMutexName(paths), out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        try
        {
            return new InstanceCoordinator(GetPidFilePath(paths), mutex);
        }
        catch
        {
            mutex.ReleaseMutex();
            mutex.Dispose();
            throw;
        }
    }

    public static int StopRunningInstance(AppPaths paths)
    {
        return StopRunningInstance(paths, Environment.ProcessPath, VerifiedInstanceProcess.Open);
    }

    internal static int StopRunningInstance(AppPaths paths, string? expectedExecutablePath,
        Func<int, IInstanceProcess> openProcess)
    {
        try
        {
            return StopSavedInstance(paths, expectedExecutablePath, openProcess);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
            Win32Exception or InvalidOperationException or NotSupportedException)
        {
            Console.Error.WriteLine($"Could not safely stop {ProductBrand.Name}: {ex.Message} " +
                "Quit the running application from its tray menu and retry.");
            return 1;
        }
    }

    private static int StopSavedInstance(AppPaths paths, string? expectedExecutablePath,
        Func<int, IInstanceProcess> openProcess)
    {
        var pidFilePath = GetPidFilePath(paths);
        if (!File.Exists(pidFilePath))
        {
            Console.WriteLine($"No running {ProductBrand.Name} instance was found.");
            return 0;
        }

        var savedText = File.ReadAllText(pidFilePath);
        if (int.TryParse(savedText.Trim(), out var legacyPid) && legacyPid > 0)
        {
            try
            {
                using var legacyProcess = openProcess(legacyPid);
            }
            catch (ArgumentException)
            {
                RemoveStaleRecord(paths, savedText);
                Console.WriteLine("The legacy PID was no longer running.");
                return 0;
            }

            Console.Error.WriteLine("The saved PID uses a legacy format without process identity. " +
                "Automatic stop was refused. Quit the application from its tray menu, or verify " +
                "its executable path in Task Manager before ending it, then retry.");
            return 1;
        }

        var savedIdentity = InstanceProcessIdentity.Parse(savedText);
        if (savedIdentity is null)
        {
            RemoveStaleRecord(paths, savedText);
            Console.WriteLine("Ignored an invalid PID identity record; no process was stopped.");
            return 0;
        }

        if (savedIdentity.ProcessId == Environment.ProcessId ||
            !InstanceProcessIdentity.PathsMatch(savedIdentity.ExecutablePath, expectedExecutablePath))
        {
            Console.Error.WriteLine("Automatic stop was refused because the saved executable identity " +
                "does not match this application. Quit the running application from its tray menu and retry.");
            return 1;
        }

        IInstanceProcess process;
        try
        {
            process = openProcess(savedIdentity.ProcessId);
        }
        catch (ArgumentException)
        {
            RemoveStaleRecord(paths, savedText);
            Console.WriteLine("The saved process was no longer running.");
            return 0;
        }

        using (process)
        {
            if (!process.Identity.Matches(savedIdentity))
            {
                RemoveStaleRecord(paths, savedText);
                Console.WriteLine("Ignored a stale or reused PID; no process was stopped.");
                return 0;
            }

            process.Kill();
            if (!process.WaitForExit(5000))
            {
                Console.Error.WriteLine("The verified process did not exit within five seconds. " +
                    "The identity record was retained; retry after it exits.");
                return 1;
            }
        }

        RemoveStaleRecord(paths, savedText);
        Console.WriteLine($"Stopped {ProductBrand.Name} (PID {savedIdentity.ProcessId}).");
        return 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            DeleteUnchangedRecord(_pidFilePath, _savedIdentity);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not remove the instance identity record: {ex.Message}");
        }
        finally
        {
            // The application lifetime disposes this coordinator on the mutex owner thread.
            _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
    }

    private static void RemoveStaleRecord(AppPaths paths, string savedText)
    {
        // Only clean up while no application owns the mutex, so a replacement cannot
        // publish its identity between our comparison and deletion.
        using var mutex = new Mutex(true, GetMutexName(paths), out var createdNew);
        if (!createdNew)
        {
            return;
        }

        try
        {
            DeleteUnchangedRecord(GetPidFilePath(paths), savedText);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    private static void DeleteUnchangedRecord(string path, string savedText)
    {
        if (File.Exists(path) && File.ReadAllText(path) == savedText)
        {
            File.Delete(path);
        }
    }

    private static string GetPidFilePath(AppPaths paths)
    {
        return Path.Combine(paths.AppDataDirectory, "codex-discord-presence.pid");
    }

    private static string GetMutexName(AppPaths paths)
    {
        var normalized = Path.GetFullPath(paths.AppDataDirectory).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return @"Local\CodexDiscordPresence_" + Convert.ToHexString(hash[..8]);
    }
}
