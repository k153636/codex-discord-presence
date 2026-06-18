namespace CodexDiscordPresence;

internal sealed class RecentEditedFileTracker
{
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, DateTime> _lastObservedEditedFiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RecentProjectFileSnapshot> _lastStableEditedFiles = Array.Empty<RecentProjectFileSnapshot>();
    private string? _lastProjectPath;

    public RecentEditedFileTracker()
        : this(() => DateTime.UtcNow)
    {
    }

    internal RecentEditedFileTracker(Func<DateTime>? utcNow = null)
    {
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<RecentProjectFileSnapshot> GetRecentEditedFiles(ProjectSnapshot? projectSnapshot)
    {
        if (projectSnapshot is null)
        {
            return Array.Empty<RecentProjectFileSnapshot>();
        }

        var now = _utcNow();
        var currentProjectPath = NormalizePath(projectSnapshot.Path);
        if (!string.Equals(currentProjectPath, _lastProjectPath, StringComparison.OrdinalIgnoreCase))
        {
            _lastProjectPath = currentProjectPath;
            _lastObservedEditedFiles.Clear();
            _lastStableEditedFiles = Array.Empty<RecentProjectFileSnapshot>();
        }

        var startupWindow = TimeSpan.FromSeconds(5);
        var changedFiles = new List<RecentProjectFileSnapshot>();
        foreach (var file in projectSnapshot.RecentFiles.OrderByDescending(file => file.LastWriteTimeUtc))
        {
            var normalizedPath = NormalizePath(file.Path);
            var isVeryRecent = now - file.LastWriteTimeUtc <= startupWindow;
            if (!_lastObservedEditedFiles.TryGetValue(normalizedPath, out var lastSeen))
            {
                if (isVeryRecent)
                {
                    changedFiles.Add(file);
                }
            }
            else if (file.LastWriteTimeUtc > lastSeen)
            {
                changedFiles.Add(file);
            }

            _lastObservedEditedFiles[normalizedPath] = file.LastWriteTimeUtc;
        }

        if (changedFiles.Count > 0)
        {
            _lastStableEditedFiles = changedFiles;
            return changedFiles;
        }

        return _lastStableEditedFiles;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .ToUpperInvariant();
        }
        catch
        {
            return path.Trim().ToUpperInvariant();
        }
    }
}
