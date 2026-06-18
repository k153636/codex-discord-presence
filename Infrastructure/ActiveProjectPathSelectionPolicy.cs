namespace CodexDiscordPresence;

public static class ActiveProjectPathSelectionPolicy
{
    private static readonly TimeSpan SwitchHysteresis = TimeSpan.FromSeconds(3);

    public static string Select(
        string currentProjectPath,
        CodexProcessSnapshot codexSnapshot,
        CodexProcessSnapshot cliSnapshot)
    {
        var currentNormalizedPath = NormalizePath(currentProjectPath);
        var candidates = new[]
        {
            CreateCandidate(codexSnapshot),
            CreateCandidate(cliSnapshot)
        }
        .Where(candidate => candidate is not null)
        .Select(candidate => candidate!)
        .Where(candidate => candidate.IsRunning)
        .ToArray();

        if (candidates.Length == 0)
        {
            return currentProjectPath;
        }

        var bestCandidate = candidates
            .OrderByDescending(candidate => candidate.LastObservedAt ?? DateTime.MinValue)
            .ThenBy(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.DetectionStrength)
            .First();

        var currentCandidate = candidates.FirstOrDefault(candidate =>
            PathEquals(candidate.ProjectPath, currentNormalizedPath));

        if (currentCandidate is not null &&
            !PathEquals(bestCandidate.ProjectPath, currentNormalizedPath) &&
            currentCandidate.LastObservedAt.HasValue &&
            bestCandidate.LastObservedAt.HasValue &&
            bestCandidate.LastObservedAt.Value - currentCandidate.LastObservedAt.Value <= SwitchHysteresis)
        {
            return currentProjectPath;
        }

        return bestCandidate.ProjectPath;
    }

    private static ObservedProjectPathCandidate? CreateCandidate(CodexProcessSnapshot snapshot)
    {
        if (string.IsNullOrWhiteSpace(snapshot.ObservedProjectPath))
        {
            return null;
        }

        var normalizedPath = NormalizePath(snapshot.ObservedProjectPath);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        return new ObservedProjectPathCandidate(
            normalizedPath,
            snapshot.LastObservedAt,
            snapshot.Confidence,
            snapshot.DetectionKind,
            snapshot.IsRunning);
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private sealed record ObservedProjectPathCandidate(
        string ProjectPath,
        DateTime? LastObservedAt,
        ActivityConfidence Confidence,
        CodexProcessDetectionKind DetectionKind,
        bool IsRunning)
    {
        public int DetectionStrength => DetectionKind switch
        {
            CodexProcessDetectionKind.CommandLine => 400,
            CodexProcessDetectionKind.ExecutablePath => 300,
            CodexProcessDetectionKind.WindowTitle => 200,
            CodexProcessDetectionKind.ProcessName => 100,
            CodexProcessDetectionKind.SessionActivity => 50,
            _ => 0
        };
    }
}
