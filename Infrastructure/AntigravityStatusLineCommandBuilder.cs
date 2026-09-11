namespace CodexDiscordPresence;

public enum AntigravityStatusLinePlatform
{
    Windows = 0,
    Unsupported = 1
}

public sealed record AntigravityStatusLineCommandBuildResult(
    bool IsSupported,
    string? Command,
    string? ScriptContent,
    string? Error);

public interface IAntigravityStatusLineCommandBuilder
{
    AntigravityStatusLineCommandBuildResult Build(AntigravityStatusLinePaths paths);
}

public sealed class AntigravityStatusLineCommandBuilder : IAntigravityStatusLineCommandBuilder
{
    private readonly AntigravityStatusLinePlatform _platform;

    public AntigravityStatusLineCommandBuilder()
        : this(OperatingSystem.IsWindows()
            ? AntigravityStatusLinePlatform.Windows
            : AntigravityStatusLinePlatform.Unsupported)
    {
    }

    internal AntigravityStatusLineCommandBuilder(AntigravityStatusLinePlatform platform)
    {
        _platform = platform;
    }

    public AntigravityStatusLineCommandBuildResult Build(AntigravityStatusLinePaths paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (_platform != AntigravityStatusLinePlatform.Windows)
        {
            return new(
                IsSupported: false,
                Command: null,
                ScriptContent: null,
                Error: "Antigravity statusLine integration is supported on Windows only.");
        }

        var command = BuildEncodedCommand(paths.ScriptPath);
        return new(
            IsSupported: true,
            Command: command,
            ScriptContent: AntigravityStatusLinePowerShellScript.Create(paths.EventFilePath),
            Error: null);
    }

    internal static string BuildLegacyQuotedCommand(string scriptPath)
    {
        var quotedScriptPath = QuoteWindowsCommandArgument(scriptPath);
        return BuildFileCommand(quotedScriptPath);
    }

    private static string BuildEncodedCommand(string scriptPath)
    {
        var invocation = $"& {QuotePowerShellString(scriptPath)}";
        var encodedInvocation = Convert.ToBase64String(
            System.Text.Encoding.Unicode.GetBytes(invocation));
        return $"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {encodedInvocation}";
    }

    private static string BuildFileCommand(string scriptPathArgument) =>
        $"powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File {scriptPathArgument}";

    private static string QuoteWindowsCommandArgument(string path) =>
        '"' + path.Replace("\"", "\\\"", StringComparison.Ordinal) + '"';

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}

internal static class AntigravityStatusLinePowerShellScript
{
    internal static string Create(string eventFilePath)
    {
        var quotedEventPath = QuotePowerShellString(eventFilePath);
        return $$"""
            $ErrorActionPreference = 'Stop'
            $MaxPayloadBytes = 262144
            $MaxEventFileBytes = 1048576
            $MaxEventCount = 64
            $MaxValueLength = 128
            $EventFilePath = {{quotedEventPath}}

            function Exit-WithStatus([string] $Value) {
                if ([string]::IsNullOrWhiteSpace($Value)) { $Value = 'Idling' }
                [Console]::Out.Write($Value)
                exit 0
            }

            function Get-SafeText([object] $Value) {
                if ($null -eq $Value -or $Value -isnot [string]) { return $null }
                $clean = -join ($Value.ToCharArray() | Where-Object { -not [char]::IsControl($_) })
                $clean = (($clean.Trim() -split '\s+') | Where-Object { $_ }) -join ' '
                if ([string]::IsNullOrWhiteSpace($clean)) { return $null }
                if ($clean.Length -gt $MaxValueLength) { return $clean.Substring(0, $MaxValueLength) }
                return $clean
            }

            function Get-SafeNonNegativeInt64([object] $Value) {
                if ($null -eq $Value) { return $null }
                try {
                    $parsed = [long] 0
                    $style = [Globalization.NumberStyles]::Integer
                    $culture = [Globalization.CultureInfo]::InvariantCulture
                    if ([long]::TryParse([string] $Value, $style, $culture, [ref] $parsed) -and $parsed -ge 0) {
                        return $parsed
                    }
                } catch { }
                return $null
            }

            function Get-PropertyValue([object] $Object, [string] $Name) {
                if ($null -eq $Object) { return $null }
                $property = $Object.PSObject.Properties[$Name]
                if ($null -eq $property) { return $null }
                return $property.Value
            }

            function Get-PathLeaf([object] $Object, [string] $Parent, [string] $Child) {
                $parentValue = Get-PropertyValue $Object $Parent
                return Get-PathLeafValue (Get-PropertyValue $parentValue $Child)
            }

            function Get-PathLeafValue([object] $Value) {
                if ($null -eq $Value -or $Value -isnot [string]) { return $null }
                $trimmed = $Value.Trim().TrimEnd('/', '\')
                if ([string]::IsNullOrWhiteSpace($trimmed)) { return $null }
                return Get-SafeText ([IO.Path]::GetFileName($trimmed))
            }

            function Get-ProjectKey([object] $Object) {
                $workspace = Get-PropertyValue $Object 'workspace'
                $rawPath = Get-PropertyValue $workspace 'project_dir'
                if ($rawPath -isnot [string] -or [string]::IsNullOrWhiteSpace($rawPath)) {
                    $rawPath = Get-PropertyValue $workspace 'current_dir'
                }
                if ($rawPath -isnot [string] -or [string]::IsNullOrWhiteSpace($rawPath)) {
                    $rawPath = Get-PropertyValue $Object 'cwd'
                }
                if ($rawPath -isnot [string] -or [string]::IsNullOrWhiteSpace($rawPath)) { return $null }
                try {
                    $candidate = $rawPath.Trim()
                    $hasDriveRoot = $candidate.Length -ge 3 -and
                        [char]::IsLetter($candidate[0]) -and
                        $candidate[1] -eq ':' -and
                        ($candidate[2] -eq '\' -or $candidate[2] -eq '/')
                    $hasUncRoot = $candidate.StartsWith('\\', [StringComparison]::Ordinal)
                    if (-not $hasDriveRoot -and -not $hasUncRoot) { return $null }
                    $normalized = [IO.Path]::GetFullPath($candidate)
                    $root = [IO.Path]::GetPathRoot($normalized)
                    if ($normalized -ine $root) { $normalized = $normalized.TrimEnd('/', '\') }
                    $bytes = [Text.Encoding]::UTF8.GetBytes($normalized.ToUpperInvariant())
                    $sha256 = [Security.Cryptography.SHA256]::Create()
                    try {
                        $hash = $sha256.ComputeHash($bytes)
                        return (-join ($hash | ForEach-Object { $_.ToString('X2') }))
                    } finally {
                        $sha256.Dispose()
                    }
                } catch { return $null }
            }

            function Read-BoundedText([string] $Path) {
                if (-not [IO.File]::Exists($Path)) { return '' }
                $length = ([IO.FileInfo] $Path).Length
                $count = [int][Math]::Min($length, $MaxEventFileBytes)
                $bytes = New-Object byte[] $count
                $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete)
                try {
                    if ($length -gt $count) { $stream.Seek(-$count, [IO.SeekOrigin]::End) | Out-Null }
                    $read = 0
                    while ($read -lt $count) {
                        $current = $stream.Read($bytes, $read, $count - $read)
                        if ($current -eq 0) { break }
                        $read += $current
                    }
                    if ($read -lt $count) { $bytes = $bytes[0..([Math]::Max(0, $read - 1))] }
                    $text = [Text.Encoding]::UTF8.GetString($bytes)
                    if ($length -gt $count) {
                        $newline = $text.IndexOf("`n")
                        if ($newline -lt 0) { return '' }
                        $text = $text.Substring($newline + 1)
                    }
                    return $text
                } finally { $stream.Dispose() }
            }

            function Test-Event([string] $Line) {
                try {
                    $event = $Line | ConvertFrom-Json -ErrorAction Stop
                    return $null -ne $event -and $event.schema_version -eq 1 -and $event.source -eq 'antigravity'
                } catch { return $false }
            }

            function Write-BoundedEvent([string] $Line) {
                $mutex = [Threading.Mutex]::new($false, 'Global\CodexDiscordPresence.AntigravityStatusLine')
                $acquired = $false
                try {
                    $acquired = $mutex.WaitOne(2000)
                    if (-not $acquired) { return }
                    $lines = @()
                    $existing = Read-BoundedText $EventFilePath
                    foreach ($candidate in ($existing -split "`n")) {
                        $candidate = $candidate.TrimEnd("`r")
                        if ($candidate -and (Test-Event $candidate)) { $lines += $candidate }
                    }
                    $lines += $Line
                    while ($lines.Count -gt $MaxEventCount -or (([Text.Encoding]::UTF8.GetByteCount(($lines -join "`n")) + 1) -gt $MaxEventFileBytes)) {
                        $lines = @($lines | Select-Object -Skip 1)
                    }
                    $directory = [IO.Path]::GetDirectoryName($EventFilePath)
                    if ($directory) { [IO.Directory]::CreateDirectory($directory) | Out-Null }
                    $temporaryPath = "$EventFilePath.$PID.tmp"
                    [IO.File]::WriteAllText($temporaryPath, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
                    Move-Item -LiteralPath $temporaryPath -Destination $EventFilePath -Force
                } finally {
                    if ($acquired) { $mutex.ReleaseMutex() }
                    $mutex.Dispose()
                }
            }

            try {
                $inputStream = [Console]::OpenStandardInput()
                $buffer = New-Object byte[] 65536
                $inputBytes = [Collections.Generic.List[byte]]::new()
                while (($read = $inputStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    if ($inputBytes.Count + $read -gt $MaxPayloadBytes) { Exit-WithStatus 'Idling' }
                    for ($index = 0; $index -lt $read; $index++) { $inputBytes.Add($buffer[$index]) }
                }
                if ($inputBytes.Count -eq 0) { Exit-WithStatus 'Idling' }
                $payload = ([Text.UTF8Encoding]::new($false, $true)).GetString($inputBytes.ToArray()) | ConvertFrom-Json -ErrorAction Stop
                if ($payload -is [array] -or $null -eq $payload) { Exit-WithStatus 'Idling' }
                $modelValue = Get-PropertyValue $payload 'model'
                $agentState = Get-SafeText (Get-PropertyValue $payload 'agent_state')
                if ($null -eq $agentState) { $agentState = 'unknown' }
                $executionMode = Get-SafeText (Get-PropertyValue $payload 'execution_mode')
                if ($executionMode -notin @('planning', 'fast')) { $executionMode = $null }
                if ($null -ne $executionMode) { $executionMode = $executionMode.ToLowerInvariant() }
                $contextWindowValue = Get-PropertyValue $payload 'context_window'
                $contextWindow = [ordered]@{
                    total_input_tokens = Get-SafeNonNegativeInt64 (Get-PropertyValue $contextWindowValue 'total_input_tokens')
                    total_output_tokens = Get-SafeNonNegativeInt64 (Get-PropertyValue $contextWindowValue 'total_output_tokens')
                }
                if ($null -eq $contextWindow.total_input_tokens -and $null -eq $contextWindow.total_output_tokens) {
                    $contextWindow = $null
                }
                $model = [ordered]@{
                    id = Get-SafeText (Get-PropertyValue $modelValue 'id')
                    display_name = Get-SafeText (Get-PropertyValue $modelValue 'display_name')
                }
                if ($null -eq $model.id -and $null -eq $model.display_name) { $model = $null }
                $workspace = [ordered]@{
                    workspace_name = Get-PathLeaf $payload 'workspace' 'current_dir'
                    project_name = Get-PathLeaf $payload 'workspace' 'project_dir'
                }
                if ($null -eq $workspace.workspace_name) {
                    $workspace.workspace_name = Get-PathLeafValue (Get-PropertyValue $payload 'cwd')
                }
                if ($null -eq $workspace.workspace_name -and $null -eq $workspace.project_name) { $workspace = $null }
                $conversationId = Get-SafeText (Get-PropertyValue $payload 'conversation_id')
                if ($null -ne $conversationId -and ($conversationId.Contains('/') -or $conversationId.Contains('\'))) { $conversationId = $null }
                $event = [ordered]@{
                    schema_version = 1
                    source = 'antigravity'
                    observed_at_utc = [DateTimeOffset]::UtcNow.ToString('O')
                    agent_state = $agentState.ToLowerInvariant()
                    model = $model
                    workspace = $workspace
                    conversation_id = $conversationId
                    execution_mode = $executionMode
                    context_window = $contextWindow
                    project_key = Get-ProjectKey $payload
                }
                $line = $event | ConvertTo-Json -Depth 8 -Compress
                if ([Text.Encoding]::UTF8.GetByteCount($line) -gt 262144) { Exit-WithStatus 'Idling' }
                Write-BoundedEvent $line
                $statusLine = switch ($agentState.ToLowerInvariant()) {
                    'thinking' { 'Thinking'; break }
                    'working' { 'Working'; break }
                    'tool_use' { 'Using tools'; break }
                    'initializing' { 'Starting'; break }
                    default { 'Idling' }
                }
                Exit-WithStatus $statusLine
            } catch {
                Exit-WithStatus 'Idling'
            }
            Exit-WithStatus 'Idling'
            """;
    }

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";
}
