using System.Diagnostics;

namespace CodexDiscordPresence;

public sealed class GitInspector
{
    public GitSnapshot GetSnapshot(string projectPath)
    {
        var output = RunGit(projectPath, "status --porcelain");
        if (output is null)
        {
            return new GitSnapshot(false, 0);
        }

        var changedFiles = output
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        return new GitSnapshot(true, changedFiles);
    }

    private static string? RunGit(string projectPath, string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = "git",
                Arguments = $"-C \"{projectPath}\" {arguments}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ParseStatusPath(string line)
    {
        if (line.Length < 4)
        {
            return null;
        }

        var path = line[3..].Trim();
        var renameArrow = path.IndexOf(" -> ", StringComparison.Ordinal);
        return renameArrow >= 0 ? path[(renameArrow + 4)..] : path;
    }
}
