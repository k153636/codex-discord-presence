param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexRpcCommand.Common.ps1')

try {
    $paths = Get-CodexRpcCommandPaths
    $root = [System.IO.Path]::GetFullPath($RootDir).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $buildScript = Join-Path $root 'build.cmd'
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "Build script not found: $buildScript"
    }

    $launchScript = Join-Path $root 'scripts\LaunchPublishedBuild.ps1'
    if (-not (Test-Path -LiteralPath $launchScript)) {
        throw "Published-build launcher not found: $launchScript"
    }

    $stopScript = Join-Path $root 'scripts\StopPublishedBuild.ps1'
    if (-not (Test-Path -LiteralPath $stopScript)) {
        throw "Published-build stop script not found: $stopScript"
    }

    $processCommonScript = Join-Path $root 'scripts\CodexRpcProcess.Common.ps1'
    if (-not (Test-Path -LiteralPath $processCommonScript)) {
        throw "Published-build process helper not found: $processCommonScript"
    }

    $publishExe = Join-Path (Join-Path $root 'publish') 'discord-presence-for-codex.exe'
    if (-not (Test-Path -LiteralPath $publishExe -PathType Leaf)) {
        throw "Published build not found: $publishExe. Run build.cmd first."
    }

    Assert-CodexRpcManagedFile -Path $paths.CommandPath -Marker $paths.Marker
    Assert-CodexRpcManagedFile -Path $paths.LauncherPath -Marker $paths.Marker
    Assert-CodexRpcManagedFile -Path $paths.QuitCommandPath -Marker $paths.Marker
    Assert-CodexRpcManagedFile -Path $paths.QuitLauncherPath -Marker $paths.Marker
    Assert-CodexRpcManagedFile -Path $paths.LegacyLauncherPath -Marker $paths.Marker
    New-Item -ItemType Directory -Force -Path $paths.BinDir | Out-Null

    $commandContent = @(
        '@echo off'
        "rem $($paths.Marker)"
        'setlocal'
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0codex-rpc-launcher.ps1" %*'
        'exit /b %ERRORLEVEL%'
    ) -join [Environment]::NewLine

    $rootLiteral = $root.Replace("'", "''")
    $launcherContent = @'
# CodexDiscordPresenceLauncher
param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments = @()
)

$ErrorActionPreference = 'Stop'
$root = '__CODEX_RPC_ROOT__'
$launchScript = Join-Path $root 'scripts\LaunchPublishedBuild.ps1'

if (-not (Test-Path -LiteralPath $launchScript)) {
    Write-Error "Codex Discord RPC checkout is unavailable: $launchScript"
    exit 1
}

$forwardedArguments = @()
if ($null -ne $Arguments) {
    $forwardedArguments = @($Arguments)
}

$launchParameters = @{
    RootDir = $root
    Arguments = $forwardedArguments
}
& $launchScript @launchParameters
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) {
    $exitCode = 0
}
exit $exitCode
'@
    $launcherContent = $launcherContent.Replace('__CODEX_RPC_ROOT__', $rootLiteral)

    $quitCommandContent = @(
        '@echo off'
        "rem $($paths.Marker)"
        'setlocal'
        'powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0codex-rpc-quit-launcher.ps1"'
        'exit /b %ERRORLEVEL%'
    ) -join [Environment]::NewLine

    $quitLauncherContent = @'
# CodexDiscordPresenceLauncher
$ErrorActionPreference = 'Stop'
$root = '__CODEX_RPC_ROOT__'
$stopScript = Join-Path $root 'scripts\StopPublishedBuild.ps1'

if (-not (Test-Path -LiteralPath $stopScript)) {
    Write-Error "Codex Discord RPC checkout is unavailable: $stopScript"
    exit 1
}

& $stopScript -RootDir $root
$exitCode = $LASTEXITCODE
if ($null -eq $exitCode) {
    $exitCode = 0
}
exit $exitCode
'@
    $quitLauncherContent = $quitLauncherContent.Replace('__CODEX_RPC_ROOT__', $rootLiteral)

    Set-Content -LiteralPath $paths.CommandPath -Value $commandContent -Encoding ascii
    Set-Content -LiteralPath $paths.LauncherPath -Value $launcherContent -Encoding utf8
    Set-Content -LiteralPath $paths.QuitCommandPath -Value $quitCommandContent -Encoding ascii
    Set-Content -LiteralPath $paths.QuitLauncherPath -Value $quitLauncherContent -Encoding utf8
    if (Test-Path -LiteralPath $paths.LegacyLauncherPath) {
        Remove-Item -LiteralPath $paths.LegacyLauncherPath -Force
    }

    $binPathKey = Get-CodexRpcCanonicalPath -Path $paths.BinDir
    $pathEntries = @(Get-CodexRpcUserPathEntries)
    $pathWithoutBin = @(
        $pathEntries | Where-Object { (Get-CodexRpcCanonicalPath -Path $_) -ne $binPathKey }
    )
    $newUserPath = (@($paths.BinDir) + $pathWithoutBin) -join ';'
    $currentUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathChanged = $currentUserPath -ne $newUserPath

    if ($pathChanged) {
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
    }

    Write-Host 'Installed the PowerShell commands: codex-rpc, codex-rpc-quit'
    Write-Host "Project root: $root"
    Write-Host "Launcher: $($paths.CommandPath)"
    if ($pathChanged) {
        Write-Host 'User PATH updated. Open a new PowerShell session before running codex-rpc.'
    }
    else {
        Write-Host 'User PATH already contains the launcher directory.'
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
