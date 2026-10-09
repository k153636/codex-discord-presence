using System.Diagnostics;
using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed record InstanceProcessIdentity(int ProcessId, string ExecutablePath, long StartedAtUtcTicks)
{
    internal static InstanceProcessIdentity? Parse(string text)
    {
        try
        {
            var identity = JsonSerializer.Deserialize<InstanceProcessIdentity>(text);
            return identity is { ProcessId: > 0, StartedAtUtcTicks: > 0 } &&
                identity.StartedAtUtcTicks <= DateTime.MaxValue.Ticks &&
                NormalizePath(identity.ExecutablePath) is not null
                ? identity
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    internal bool Matches(InstanceProcessIdentity other)
    {
        return ProcessId == other.ProcessId && StartedAtUtcTicks == other.StartedAtUtcTicks &&
            PathsMatch(ExecutablePath, other.ExecutablePath);
    }

    internal static bool PathsMatch(string? first, string? second)
    {
        var normalizedFirst = NormalizePath(first);
        var normalizedSecond = NormalizePath(second);
        return normalizedFirst is not null && normalizedSecond is not null &&
            string.Equals(normalizedFirst, normalizedSecond, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }
}

internal interface IInstanceProcess : IDisposable
{
    InstanceProcessIdentity Identity { get; }
    void Kill();
    bool WaitForExit(int milliseconds);
}

internal sealed class VerifiedInstanceProcess : IInstanceProcess
{
    private readonly Process _process;

    private VerifiedInstanceProcess(Process process)
    {
        _process = process;
        // Retain the process handle before reading identity. Kill uses this same handle,
        // so PID reuse after verification cannot redirect termination to a new process.
        _ = process.SafeHandle;
        var executablePath = process.MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(executablePath) || !Path.IsPathFullyQualified(executablePath))
        {
            throw new InvalidOperationException("The process executable path could not be verified.");
        }

        Identity = new InstanceProcessIdentity(process.Id, Path.GetFullPath(executablePath),
            process.StartTime.ToUniversalTime().Ticks);
    }

    public InstanceProcessIdentity Identity { get; }

    internal static VerifiedInstanceProcess Open(int processId)
    {
        var process = Process.GetProcessById(processId);
        try
        {
            return new VerifiedInstanceProcess(process);
        }
        catch
        {
            process.Dispose();
            throw;
        }
    }

    public void Kill() => _process.Kill(entireProcessTree: false);
    public bool WaitForExit(int milliseconds) => _process.WaitForExit(milliseconds);
    public void Dispose() => _process.Dispose();
}
