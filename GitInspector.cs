using System.Diagnostics;

namespace CodexDiscordPresence;

public sealed class GitInspector
{
    public GitSnapshot GetSnapshot(string projectPath)
    {
        var output = RunGit(projectPath, ["-C", projectPath, "status", "--porcelain=v1"]);
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

    private static string? RunGit(string projectPath, IReadOnlyList<string> arguments)
    {
        try
        {
            if (!Directory.Exists(projectPath))
            {
                return null;
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            using var process = Process.Start(startInfo);

            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                    // Ignore cleanup failures after a git timeout.
                }

                return null;
            }

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
