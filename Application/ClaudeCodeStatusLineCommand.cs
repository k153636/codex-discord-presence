using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace CodexDiscordPresence;

internal static class ClaudeCodeStatusLineCommand
{
    internal static async Task<int> RunAsync()
    {
        using var input = Console.OpenStandardInput();
        using var output = Console.OpenStandardOutput();
        using var error = Console.OpenStandardError();
        using var payload = new MemoryStream();
        await input.CopyToAsync(payload);
        var directory = Path.Combine(AppPaths.Create(AppProfileKind.Codex).AppDataDirectory, "claude-code");
        try
        {
            var observation = ClaudeCodeUsageObservation.Parse(Encoding.UTF8.GetString(payload.GetBuffer(), 0, (int)payload.Length), DateTimeOffset.UtcNow);
            if (observation is not null) new ClaudeCodeUsageStore(Path.Combine(directory, "usage")).Write(observation);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Usage capture must never interfere with the user's terminal display.
        }
        try
        {
            var original = ClaudeCodeStatusLineInstaller.ReadOriginalCommand(Path.Combine(directory, "status-line-owner.json"));
            return original is null ? 0 : await RelayAsync(original, payload.ToArray(), output, error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or Win32Exception)
        {
            return 0;
        }
    }

    internal static async Task<int> RelayAsync(string command, byte[] payload, Stream output, Stream error)
    {
        using var process = new Process { StartInfo = CreateStartInfo(command) };
        process.Start();
        var outputTask = process.StandardOutput.BaseStream.CopyToAsync(output);
        var errorTask = process.StandardError.BaseStream.CopyToAsync(error);
        try
        {
            try
            {
                await process.StandardInput.BaseStream.WriteAsync(payload);
            }
            catch (IOException)
            {
                // Some status lines intentionally ignore stdin. Preserve their output and exit status.
            }
            finally { process.StandardInput.Close(); }
            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());
            return process.ExitCode;
        }
        finally { if (!process.HasExited) process.Kill(entireProcessTree: true); }
    }

    private static ProcessStartInfo CreateStartInfo(string command)
    {
        var bash = FindGitBash();
        var info = new ProcessStartInfo(bash ?? "powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        if (bash is not null) info.ArgumentList.Add("-c");
        else
        {
            info.ArgumentList.Add("-NoLogo");
            info.ArgumentList.Add("-NoProfile");
            info.ArgumentList.Add("-NonInteractive");
            info.ArgumentList.Add("-Command");
        }
        info.ArgumentList.Add(command);
        return info;
    }

    private static string? FindGitBash()
    {
        var configured = Environment.GetEnvironmentVariable("CLAUDE_CODE_GIT_BASH_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var candidates = new List<string>
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Git", "bin", "bash.exe")
        };
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (File.Exists(Path.Combine(directory, "git.exe")))
                candidates.Add(Path.GetFullPath(Path.Combine(directory, "..", "bin", "bash.exe")));
        }
        return candidates.FirstOrDefault(File.Exists);
    }
}
