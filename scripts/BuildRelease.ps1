[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [string]$ReleaseNotes,
    [string]$PreviousReleaseDirectory
)

$ErrorActionPreference = 'Stop'
$repoDirectory = Split-Path -Parent $PSScriptRoot
$projectFile = Join-Path $repoDirectory 'discord-presence-for-codex.csproj'
[xml]$project = Get-Content -LiteralPath $projectFile
if (-not $Version) { $Version = [string]$project.Project.PropertyGroup.Version }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Release versions must have three numeric components.' }

# A unique staging directory avoids replacing a running development executable.
$buildId = $Version + '-' + [guid]::NewGuid().ToString('N')
$stagingDirectory = Join-Path $repoDirectory ('publish-release\' + $buildId)
$releaseDirectory = Join-Path $repoDirectory ('Releases\' + $buildId)
New-Item -ItemType Directory -Path $stagingDirectory -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDirectory -Force | Out-Null
if ($PreviousReleaseDirectory) {
    Get-ChildItem -LiteralPath $PreviousReleaseDirectory -File |
        Where-Object { $_.Extension -eq '.nupkg' -or $_.Name -eq 'releases.win.json' } |
        Copy-Item -Destination $releaseDirectory
}
Push-Location $repoDirectory
try {
    dotnet tool restore
    if ($LASTEXITCODE -ne 0) { throw 'The pinned Velopack packaging tool could not be restored.' }
    dotnet publish $projectFile -c Release -r win-x64 --self-contained false -p:Version=$Version -o $stagingDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Release publish failed.' }
    foreach ($name in @('appsettings.json', 'appsettings.cli.json')) {
        Copy-Item -LiteralPath (Join-Path $stagingDirectory $name) -Destination (Join-Path $stagingDirectory ($name.Replace('.json', '.defaults.json')))
    }
    Copy-Item -LiteralPath (Join-Path $repoDirectory 'LICENSE') -Destination $stagingDirectory
    $packArguments = @('pack', '--packId', 'K.CodePresence', '--packVersion', $Version,
        '--packTitle', "K's Code Presence", '--packDir', $stagingDirectory,
        '--mainExe', 'discord-presence-for-codex.exe', '--channel', 'win', '--runtime', 'win-x64',
        '--framework', 'net9.0-x64-desktop', '--shortcuts', 'StartMenuRoot', '--outputDir', $releaseDirectory)
    if ($ReleaseNotes) { $packArguments += @('--releaseNotes', (Resolve-Path -LiteralPath $ReleaseNotes).Path) }
    dotnet tool run vpk -- @packArguments
    if ($LASTEXITCODE -ne 0) { throw 'Velopack packaging failed.' }
    Write-Host "Release packages: $releaseDirectory"
}
finally { Pop-Location }
