using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class ClaudeCodeUsageStore(string directory)
{
    internal void Write(ClaudeCodeUsageObservation observation)
    {
        Directory.CreateDirectory(directory);
        var key = Key(observation.SessionId);
        using var mutex = new Mutex(false, "Local\\CodexDiscordPresence.ClaudeCode.Usage." + key);
        var acquired = false;
        try
        {
            try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(2)); }
            catch (AbandonedMutexException) { acquired = true; }
            if (!acquired) throw new IOException("Claude usage writer is busy.");
            var path = Path.Combine(directory, key + ".json");
            if (Read(path) is { } previous && previous.ObservedAtUtc > observation.ObservedAtUtc) return;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(observation), new UTF8Encoding(false));
                File.Move(temporary, path, overwrite: true);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        finally { if (acquired) mutex.ReleaseMutex(); }
    }

    internal ClaudeCodeUsageObservation? GetForSession(ClaudeCodeSessionObservation session, DateTimeOffset nowUtc)
    {
        var usage = Read(Path.Combine(directory, Key(session.SessionId) + ".json"));
        return usage is not null && !session.Ended && usage.SessionId == session.SessionId &&
            usage.ObservedAtUtc != default && usage.ObservedAtUtc <= nowUtc &&
            new ProviderWorkspaceObservation(usage.ProjectPath, null, null).MatchesProjectPath(session.ProjectPath)
            ? usage : null;
    }

    private static string Key(string sessionId) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));

    private static ClaudeCodeUsageObservation? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 16_384) return null;
            var usage = JsonSerializer.Deserialize<ClaudeCodeUsageObservation>(File.ReadAllText(path));
            return usage is not null && ClaudeCodeHookParser.SafeText(usage.SessionId) is not null &&
                ClaudeCodeHookParser.SafeText(usage.ProjectPath, 4096) is not null && Path.IsPathFullyQualified(usage.ProjectPath) &&
                usage.EstimatedCostUsd is not < 0 && (usage.RateLimit is null ||
                    usage.RateLimit is { UsedPercent: >= 0 and <= 100, WindowDurationMinutes: 300 }) ? usage : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
    }
}
