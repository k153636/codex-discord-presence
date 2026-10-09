using System.Text;

namespace CodexDiscordPresence;

internal static class ClaudeCodeNativeCommand
{
    internal const string HookOwnershipMarker = "CodexDiscordPresence.ClaudeCodeHook.v1";
    internal const string StatusLineOwnershipMarker = "CodexDiscordPresence.ClaudeCodeStatusLine.v1";

    internal static string Create(string executablePath, string argument)
    {
        var marker = argument switch
        {
            "--claude-hook" => HookOwnershipMarker,
            "--claude-statusline" => StatusLineOwnershipMarker,
            _ => throw new ArgumentOutOfRangeException(nameof(argument))
        };
        // PowerShell's text pipeline uses a legacy code page. Forward native bytes unchanged.
        var script = $$"""
            # {{marker}}
            $ProgressPreference = 'SilentlyContinue'
            $p = New-Object System.Diagnostics.Process
            $p.StartInfo.FileName = '{{executablePath.Replace("'", "''", StringComparison.Ordinal)}}'
            $p.StartInfo.Arguments = '{{argument}}'
            $p.StartInfo.UseShellExecute = $false
            $p.StartInfo.CreateNoWindow = $true
            $p.StartInfo.RedirectStandardInput = $true
            $p.StartInfo.RedirectStandardOutput = $true
            $p.StartInfo.RedirectStandardError = $true
            [void]$p.Start()
            $outTask = $p.StandardOutput.BaseStream.CopyToAsync([Console]::OpenStandardOutput())
            $errTask = $p.StandardError.BaseStream.CopyToAsync([Console]::OpenStandardError())
            [Console]::OpenStandardInput().CopyTo($p.StandardInput.BaseStream)
            $p.StandardInput.Close()
            [void]$outTask.GetAwaiter().GetResult()
            [void]$errTask.GetAwaiter().GetResult()
            $p.WaitForExit()
            exit {{(argument == "--claude-hook" ? "0" : "$p.ExitCode")}}
            """;
        // Hook ownership recognizes the original LF-terminated marker across upgrades.
        script = script.Replace("\r\n", "\n", StringComparison.Ordinal);
        return "powershell.exe -NoLogo -NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " +
            Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }
}
