using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

internal sealed class ClaudeCodeObservationStore(string directory)
{
    internal string DirectoryPath { get; } = directory;

    internal bool HasSessionEvidence(string sessionId)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sessionId)));
        return Read(Path.Combine(DirectoryPath, key + ".json")) is not null;
    }

    internal void Write(ClaudeCodeHookEvent hookEvent)
    {
        Directory.CreateDirectory(DirectoryPath);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(hookEvent.SessionId)));
        using var mutex = new Mutex(false, "Local\\CodexDiscordPresence.ClaudeCode." + key);
        var acquired = false;
        try
        {
            try
            {
                acquired = mutex.WaitOne(TimeSpan.FromSeconds(2));
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
            if (!acquired)
            {
                throw new IOException("Claude observation writer is busy.");
            }
            var path = Path.Combine(DirectoryPath, key + ".json");
            var previous = Read(path);
            var observation = ClaudeCodeSessionObservation.Apply(previous, hookEvent);
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(observation), new UTF8Encoding(false));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (acquired)
            {
                mutex.ReleaseMutex();
            }
        }
    }

    internal ClaudeCodeSessionObservation? Select(string projectPath, DateTimeOffset nowUtc, TimeSpan freshness)
    {
        if (!Directory.Exists(DirectoryPath))
        {
            return null;
        }
        return Directory.EnumerateFiles(DirectoryPath, "*.json")
            .Select(Read)
            .OfType<ClaudeCodeSessionObservation>()
            .Where(observation => IsEligible(observation, observation.ProjectPath, nowUtc, freshness))
            .OrderByDescending(observation => new ProviderWorkspaceObservation(observation.ProjectPath, null, null).MatchesProjectPath(projectPath))
            .ThenByDescending(observation => observation.ObservedAtUtc)
            .FirstOrDefault();
    }

    internal static bool IsEligible(ClaudeCodeSessionObservation observation, string projectPath,
        DateTimeOffset nowUtc, TimeSpan freshness)
    {
        var age = nowUtc - observation.ObservedAtUtc;
        return !observation.Ended && age >= TimeSpan.Zero && age <= freshness &&
            new ProviderWorkspaceObservation(observation.ProjectPath, null, null).MatchesProjectPath(projectPath);
    }

    private static ClaudeCodeSessionObservation? Read(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length > 131_072)
            {
                return null;
            }
            var observation = JsonSerializer.Deserialize<ClaudeCodeSessionObservation>(File.ReadAllText(path));
            return observation is { Tools: not null, ActiveAgentIds: not null } &&
                !string.IsNullOrWhiteSpace(observation.SessionId) && !string.IsNullOrWhiteSpace(observation.ProjectPath) &&
                observation.Tools.Count <= 64 && observation.ActiveAgentIds.Count <= 64
                ? observation
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }
}
