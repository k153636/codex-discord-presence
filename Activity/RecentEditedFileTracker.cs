namespace CodexDiscordPresence;

internal sealed class RecentEditedFileTracker
{
    private readonly Dictionary<string, DateTime> _lastObservedEditedFiles = new(StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<RecentProjectFileSnapshot> GetRecentEditedFiles(ProjectSnapshot? projectSnapshot)
    {
        if (projectSnapshot is null)
        {
            return Array.Empty<RecentProjectFileSnapshot>();
        }

        var now = DateTime.UtcNow;
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

        return changedFiles;
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
