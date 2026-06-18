namespace CodexDiscordPresence;

public sealed class ProjectInspector
{
    private readonly ProjectOptions _options;
    private readonly HashSet<string> _ignoredDirectories;
    private readonly string[] _ignoredFilePatterns;

    public ProjectInspector(ProjectOptions options)
    {
        _options = options;
        ProjectPath = ResolveProjectPath(options);
        _ignoredDirectories = options.IgnoredDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
        _ignoredFilePatterns = options.IgnoredFilePatterns
            .Where(pattern => !string.IsNullOrWhiteSpace(pattern))
            .ToArray();
    }

    public string ProjectPath { get; }

    public ProjectSnapshot GetSnapshot()
    {
        var directory = new DirectoryInfo(ProjectPath);
        var projectName = string.IsNullOrWhiteSpace(_options.DisplayName)
            ? directory.Name
            : _options.DisplayName;

        var inspection = directory.Exists ? InspectProject(directory) : ProjectInspection.Empty;

        return new ProjectSnapshot(
            projectName,
            ProjectPath,
            inspection.RecentFile?.Name,
            inspection.RecentFile?.FullName,
            inspection.TotalFileCount,
            inspection.ScannedFileCount,
            inspection.TotalLineCount,
            inspection.RecentFiles);
    }

    private ProjectInspection InspectProject(DirectoryInfo root)
    {
        FileInfo? best = null;
        var totalFileCount = 0;
        var scannedFileCount = 0;
        long totalLineCount = 0;
        var recentFiles = new List<FileInfo>();
        var pending = new Queue<(DirectoryInfo Directory, int Depth)>();
        pending.Enqueue((root, 0));

        while (pending.TryDequeue(out var item))
        {
            if (item.Depth > _options.RecentFileSearchDepth)
            {
                continue;
            }

            IEnumerable<FileInfo> files = [];
            IEnumerable<DirectoryInfo> directories = [];

            try
            {
                files = item.Directory.EnumerateFiles();
                directories = item.Directory.EnumerateDirectories();
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                if (IsIgnoredFile(file.Name))
                {
                    continue;
                }

                totalFileCount++;

                if (scannedFileCount < _options.MaxProjectFilesToScan)
                {
                    scannedFileCount++;
                    totalLineCount += TryCountLines(file);
                }

                if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc)
                {
                    best = file;
                }

                TrackRecentFile(recentFiles, file);
            }

            foreach (var child in directories)
            {
                if (!_ignoredDirectories.Contains(child.Name))
                {
                    pending.Enqueue((child, item.Depth + 1));
                }
            }
        }

        return new ProjectInspection(
            best,
            totalFileCount,
            scannedFileCount,
            totalLineCount,
            recentFiles
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Select(file => new RecentProjectFileSnapshot(file.Name, file.FullName, file.LastWriteTimeUtc))
                .ToArray());
    }

    private bool IsIgnoredFile(string fileName)
    {
        foreach (var pattern in _ignoredFilePatterns)
        {
            if (System.IO.Enumeration.FileSystemName.MatchesSimpleExpression(pattern, fileName, ignoreCase: true))
            {
                return true;
            }
        }

        return false;
    }

    private static string ResolveProjectPath(ProjectOptions options)
    {
        var resolvedPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.Path));
        if (!options.PreferGitRootForProjectPath)
        {
            return resolvedPath;
        }

        var directory = new DirectoryInfo(resolvedPath);
        while (directory != null)
        {
            if (Directory.Exists(Path.Combine(directory.FullName, ".git")) ||
                File.Exists(Path.Combine(directory.FullName, ".git")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return resolvedPath;
    }

    private long TryCountLines(FileInfo file)
    {
        if (file.Length > _options.MaxLineCountFileBytes)
        {
            return 0;
        }

        try
        {
            long lines = 0;
            var sawCharacters = false;
            var lastWasNewLine = false;
            using var stream = file.Open(FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream, detectEncodingFromByteOrderMarks: true);

            var buffer = new char[8192];
            int read;
            while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
            {
                for (var i = 0; i < read; i++)
                {
                    sawCharacters = true;
                    lastWasNewLine = buffer[i] == '\n';
                    if (buffer[i] == '\n')
                    {
                        lines++;
                    }
                }
            }

            if (sawCharacters && !lastWasNewLine)
            {
                lines++;
            }

            return lines;
        }
        catch
        {
            return 0;
        }
    }

    private void TrackRecentFile(List<FileInfo> recentFiles, FileInfo file)
    {
        recentFiles.Add(file);
        recentFiles.Sort((left, right) => right.LastWriteTimeUtc.CompareTo(left.LastWriteTimeUtc));

        var maxTracked = Math.Max(1, _options.MaxRecentEditedFilesToTrack);
        if (recentFiles.Count > maxTracked)
        {
            recentFiles.RemoveRange(maxTracked, recentFiles.Count - maxTracked);
        }
    }

    private sealed record ProjectInspection(
        FileInfo? RecentFile,
        int TotalFileCount,
        int ScannedFileCount,
        long TotalLineCount,
        IReadOnlyList<RecentProjectFileSnapshot> RecentFiles)
    {
        public static ProjectInspection Empty { get; } = new(null, 0, 0, 0, Array.Empty<RecentProjectFileSnapshot>());
    }
}
