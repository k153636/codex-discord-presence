using System.Diagnostics;

namespace CodexDiscordPresence;

public sealed class GitInspector
{
    public GitSnapshot GetSnapshot(string projectPath, CancellationToken cancellationToken = default)
    {
        var output = RunGit(
            projectPath,
            ["-C", projectPath, "status", "--porcelain=v1"],
            cancellationToken);
        if (output is null)
        {
            return new GitSnapshot(false, 0, null);
        }

        var latestCommitMessage = RunGit(
            projectPath,
            ["-C", projectPath, "log", "-1", "--pretty=%s"],
            cancellationToken)?.Trim();
        var createdFileCount = CountCreatedFiles(output);
        var deletedFileCount = CountDeletedFiles(output);

        return new GitSnapshot(
            true,
            CountChangedFiles(output),
            string.IsNullOrWhiteSpace(latestCommitMessage) ? null : latestCommitMessage,
            createdFileCount,
            deletedFileCount);
    }

    internal static int CountChangedFiles(string output)
    {
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(ParseStatusPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
    }

    public static int CountCreatedFiles(string output)
    {
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(IsCreatedStatusLine);
    }

    public static int CountDeletedFiles(string output)
    {
        return output
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Count(IsDeletedStatusLine);
    }

    private static string? RunGit(
        string projectPath,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

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

            process = Process.Start(startInfo);

            if (process is null)
            {
                return null;
            }

            // Read both streams while polling so a stalled Git process cannot block
            // shutdown on ReadToEnd, and cancellation can terminate it promptly.
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            var deadlineUtc = DateTime.UtcNow.AddSeconds(3);
            while (!process.WaitForExit(100))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (DateTime.UtcNow >= deadlineUtc)
                {
                    TerminateProcess(process);
                    return null;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            var output = outputTask.GetAwaiter().GetResult();
            _ = errorTask.GetAwaiter().GetResult();
            return process.ExitCode == 0 ? output : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TerminateProcess(process);
            throw;
        }
        catch
        {
            TerminateProcess(process);
            return null;
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static void TerminateProcess(Process? process)
    {
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(500);
            }
        }
        catch
        {
            // Ignore cleanup failures after a Git timeout or cancellation.
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

    private static bool IsCreatedStatusLine(string line)
    {
        return HasStatus(line, 'A') || HasStatus(line, '?') || HasStatus(line, 'C');
    }

    private static bool IsDeletedStatusLine(string line)
    {
        return HasStatus(line, 'D');
    }

    private static bool HasStatus(string line, char status)
    {
        return line.Length >= 2 &&
            (line[0] == status || line[1] == status);
    }
}
