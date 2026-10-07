using System.ComponentModel;
using System.Diagnostics;

namespace CodexDiscordPresence;

internal sealed class ClaudeCodeSpinnerReader(string executablePath)
{
    private DateTimeOffset _lastReadUtc;
    private string? _lastSessionId;
    private string? _label;

    internal async Task<string?> ReadAsync(string sessionId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (_lastSessionId == sessionId && now - _lastReadUtc < TimeSpan.FromSeconds(3))
        {
            return _label;
        }
        _lastSessionId = sessionId;
        _lastReadUtc = now;
        _label = null;
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executablePath)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    ArgumentList = { "--claude-spinner" }
                }
            };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            try
            {
                // Only the extracted label crosses the helper boundary, never terminal contents.
                var output = await process.StandardOutput.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                if (process.ExitCode == 0 && output.Length <= 48 && ClaudeCodeHookParser.SafeText(output, 48) is { } safe)
                {
                    _label = safe;
                }
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException)
        {
            // An inaccessible or disappearing terminal leaves lifecycle-based labels intact.
            _label = null;
        }
        return _label;
    }
}
