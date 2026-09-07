namespace CodexDiscordPresence;

public static class ActiveProjectPathSelectionPolicy
{
    private static readonly TimeSpan SwitchHysteresis = TimeSpan.FromSeconds(3);
    private static readonly string[] ProjectMarkerFileNames =
    [
        "package.json",
        "pyproject.toml",
        "Cargo.toml",
        "go.mod",
        "pom.xml",
        "build.gradle",
        "build.gradle.kts",
        "settings.gradle",
        "settings.gradle.kts",
        "composer.json",
        "Gemfile",
        "CMakeLists.txt",
        "global.json",
        "Directory.Build.props",
        "Directory.Build.targets"
    ];
    private static readonly string[] ProjectMarkerFilePatterns =
    [
        "*.sln",
        "*.slnx",
        "*.csproj",
        "*.fsproj",
        "*.vbproj",
        "*.vcxproj",
        "*.uproject",
        "*.slnf"
    ];

    public static string Select(
        string currentProjectPath,
        string? focusedProjectPath,
        CodexProcessSnapshot codexSnapshot,
        CodexProcessSnapshot cliSnapshot)
    {
        if (TryNormalizeFocusedProjectPath(focusedProjectPath, out var normalizedFocusPath, out _))
        {
            return normalizedFocusPath;
        }

        return Select(currentProjectPath, codexSnapshot, cliSnapshot);
    }

    public static bool TryNormalizeFocusedProjectPath(
        string? focusedProjectPath,
        out string normalizedPath,
        out string rejectionReason)
    {
        normalizedPath = "";
        rejectionReason = "";

        if (string.IsNullOrWhiteSpace(focusedProjectPath))
        {
            rejectionReason = "no focused project path";
            return false;
        }

        try
        {
            normalizedPath = Path.GetFullPath(focusedProjectPath);
        }
        catch
        {
            rejectionReason = "focused project path could not be normalized";
            return false;
        }

        normalizedPath = normalizedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (!Directory.Exists(normalizedPath))
        {
            rejectionReason = "focused project path does not exist";
            normalizedPath = "";
            return false;
        }

        if (IsKnownBroadContainerPath(normalizedPath) && !HasProjectMarkers(normalizedPath))
        {
            rejectionReason = "focused project path is a broad container path";
            normalizedPath = "";
            return false;
        }

        return true;
    }

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

    private static bool HasProjectMarkers(string path)
    {
        try
        {
            if (Directory.Exists(Path.Combine(path, ".git")) || File.Exists(Path.Combine(path, ".git")))
            {
                return true;
            }

            foreach (var exactFileName in ProjectMarkerFileNames)
            {
                if (File.Exists(Path.Combine(path, exactFileName)))
                {
                    return true;
                }
            }

            foreach (var pattern in ProjectMarkerFilePatterns)
            {
                if (Directory.EnumerateFiles(path, pattern, SearchOption.TopDirectoryOnly).Any())
                {
                    return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static bool IsKnownBroadContainerPath(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return false;
        }

        var root = Path.GetPathRoot(normalizedPath);
        if (!string.IsNullOrWhiteSpace(root) &&
            string.Equals(normalizedPath, NormalizePath(root), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var broadPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)
        };

        return broadPaths
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(NormalizePath)
            .Any(candidate => string.Equals(candidate, normalizedPath, StringComparison.OrdinalIgnoreCase));
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
