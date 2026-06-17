namespace CodexDiscordPresence;

public sealed class ProjectInspector
{
    private readonly ProjectOptions _options;
    private readonly HashSet<string> _ignoredDirectories;

    public ProjectInspector(ProjectOptions options)
    {
        _options = options;
        ProjectPath = Path.GetFullPath(options.Path);
        _ignoredDirectories = options.IgnoredDirectories.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public string ProjectPath { get; }

    public ProjectSnapshot GetSnapshot()
    {
        var directory = new DirectoryInfo(ProjectPath);
        var projectName = string.IsNullOrWhiteSpace(_options.DisplayName)
            ? directory.Name
            : _options.DisplayName;

        var recentFile = directory.Exists ? FindMostRecentFile(directory) : null;

        return new ProjectSnapshot(
            projectName,
            ProjectPath,
            recentFile?.Name,
            recentFile?.FullName);
    }

    private FileInfo? FindMostRecentFile(DirectoryInfo root)
    {
        FileInfo? best = null;
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
                if (best is null || file.LastWriteTimeUtc > best.LastWriteTimeUtc)
                {
                    best = file;
                }
            }

            foreach (var child in directories)
            {
                if (!_ignoredDirectories.Contains(child.Name))
                {
                    pending.Enqueue((child, item.Depth + 1));
                }
            }
        }

        return best;
    }
}
