namespace CodexDiscordPresence;

internal sealed record EditedFileSelection(
    RecentProjectFileSnapshot? ActiveFile,
    int RecentFileCount,
    int TotalFileCount,
    bool IsDirectToolTarget);

internal static class EditedFileSelector
{
    public static EditedFileSelection Select(PresenceContext context, int freshnessSeconds)
    {
        var recentFiles = context.Codex.RecentEditedFiles
            .Where(file => !string.IsNullOrWhiteSpace(file.Path) || !string.IsNullOrWhiteSpace(file.Name))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .GroupBy(GetFileIdentity, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var directFilePath = SelectFreshDirectToolFilePath(context, freshnessSeconds);
        if (directFilePath is null)
        {
            return new EditedFileSelection(
                recentFiles.FirstOrDefault(),
                recentFiles.Length,
                recentFiles.Length,
                false);
        }

        var matchingRecentFile = recentFiles.FirstOrDefault(file =>
            string.Equals(
                ResolveFilePath(context.Project.Path, file.Path),
                directFilePath,
                StringComparison.OrdinalIgnoreCase));
        if (matchingRecentFile is not null)
        {
            return new EditedFileSelection(
                matchingRecentFile,
                recentFiles.Length,
                recentFiles.Length,
                true);
        }

        var directFile = new RecentProjectFileSnapshot(
            Path.GetFileName(directFilePath),
            directFilePath,
            context.Codex.LastDirectToolFileAt ?? DateTime.UtcNow);
        return new EditedFileSelection(
            directFile,
            recentFiles.Length,
            recentFiles.Length + 1,
            true);
    }

    public static string FormatForDisplay(ProjectSnapshot project, RecentProjectFileSnapshot file)
    {
        var filePath = ResolveFilePath(project.Path, file.Path);
        // Discord's activity line should identify the active file, not its
        // parent folders or the path history that led to it.
        var safeName = Path.GetFileName(file.Name);
        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = Path.GetFileName(filePath ?? file.Path);
        }

        return string.IsNullOrWhiteSpace(safeName) ? "file" : safeName;
    }

    private static string? SelectFreshDirectToolFilePath(PresenceContext context, int freshnessSeconds)
    {
        if (context.Codex.ActivityKind is not (CodexActivityKind.ApplyingEdits or CodexActivityKind.CoordinatingChanges or CodexActivityKind.CreatingFiles or CodexActivityKind.DeletingFiles) ||
            string.IsNullOrWhiteSpace(context.Codex.LastDirectToolFilePath) ||
            !context.Codex.LastDirectToolFileAt.HasValue)
        {
            return null;
        }

        var elapsed = DateTime.UtcNow - context.Codex.LastDirectToolFileAt.Value;
        var freshnessWindow = TimeSpan.FromSeconds(Math.Max(1, freshnessSeconds));
        var belongsToCurrentTask = context.Codex.LastTaskStartedAt.HasValue &&
            context.Codex.LastDirectToolFileAt.Value >= context.Codex.LastTaskStartedAt.Value;
        if ((elapsed > freshnessWindow && !belongsToCurrentTask) || elapsed < TimeSpan.FromSeconds(-30))
        {
            return null;
        }

        return ResolveFilePath(context.Project.Path, context.Codex.LastDirectToolFilePath);
    }

    private static string GetFileIdentity(RecentProjectFileSnapshot file)
    {
        if (!string.IsNullOrWhiteSpace(file.Path))
        {
            return ResolveFilePath(null, file.Path) ?? file.Path;
        }

        return file.Name;
    }

    private static string? ResolveFilePath(string? projectPath, string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(
                Path.IsPathRooted(filePath) || string.IsNullOrWhiteSpace(projectPath)
                    ? filePath
                    : Path.Combine(projectPath, filePath));
        }
        catch
        {
            return filePath.Trim();
        }
    }

}
