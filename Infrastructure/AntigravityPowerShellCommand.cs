namespace CodexDiscordPresence;

internal static class AntigravityPowerShellCommand
{
    internal static string BuildEncoded(string scriptPath, params string[] arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scriptPath);
        ArgumentNullException.ThrowIfNull(arguments);

        var invocation = "& " + Quote(scriptPath) + string.Concat(
            arguments.Select(argument => " " + Quote(argument)));
        var encodedInvocation = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(invocation));
        return $"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedInvocation}";
    }

    private static string Quote(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
