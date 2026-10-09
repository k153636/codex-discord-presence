param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) {
    $OutputDirectory = Join-Path $root ('Releases\standalone-' + [Guid]::NewGuid().ToString('N'))
}
if (Test-Path -LiteralPath $OutputDirectory) { throw 'Use a new output directory to preserve earlier builds.' }
dotnet publish (Join-Path $root 'discord-presence-for-codex.csproj') -c Release -r win-x64 --self-contained false -p:StandaloneDistribution=true -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory
if ($LASTEXITCODE -ne 0) { throw 'Standalone publish failed.' }
$files = @(Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse)
if ($files.Count -ne 1 -or $files[0].Name -ne 'discord-presence-for-codex.exe') {
    throw 'Standalone distribution must contain exactly one executable.'
}
Write-Output $files[0].FullName
