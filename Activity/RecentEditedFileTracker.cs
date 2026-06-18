namespace CodexDiscordPresence;

internal sealed class RecentEditedFileTracker
{
    private readonly TimeSpan _retentionWindow;
    private readonly Func<DateTime> _utcNow;
    private readonly Dictionary<string, DateTime> _lastObservedEditedFiles = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<RecentProjectFileSnapshot> _lastStableEditedFiles = Array.Empty<RecentProjectFileSnapshot>();
    private DateTime _lastChangedAtUtc = DateTime.MinValue;

    public RecentEditedFileTracker()
        : this(TimeSpan.FromSeconds(15))
    {
    }

    internal RecentEditedFileTracker(TimeSpan retentionWindow, Func<DateTime>? utcNow = null)
    {
        _retentionWindow = retentionWindow > TimeSpan.Zero ? retentionWindow : TimeSpan.FromSeconds(15);
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    public IReadOnlyList<RecentProjectFileSnapshot> GetRecentEditedFiles(ProjectSnapshot? projectSnapshot)
    {
        if (projectSnapshot is null)
        {
            return Array.Empty<RecentProjectFileSnapshot>();
        }

        var now = _utcNow();
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
            _lastChangedAtUtc = now;
            return changedFiles;
        }

        if (_lastStableEditedFiles.Count > 0 &&
            now - _lastChangedAtUtc <= _retentionWindow)
        {
            return _lastStableEditedFiles;
        }

        _lastStableEditedFiles = Array.Empty<RecentProjectFileSnapshot>();
        return Array.Empty<RecentProjectFileSnapshot>();
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
