param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = 'Stop'

function Get-RunningPresenceProcesses {
    @(Get-Process -Name 'discord-presence-for-codex' -ErrorAction SilentlyContinue)
}

function Wait-ForPresenceToStop {
    param(
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $running = Get-RunningPresenceProcesses
        if ($running.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    $pids = (Get-RunningPresenceProcesses | Select-Object -ExpandProperty Id) -join ', '
    throw "The previous Codex Discord RPC process did not exit within $TimeoutSeconds seconds. PID(s): $pids"
}

try {
    $root = [System.IO.Path]::GetFullPath($RootDir).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
    $publishDir = Join-Path $root 'publish'
    $publishExe = Join-Path $publishDir 'discord-presence-for-codex.exe'
    $stagingExe = Join-Path (Join-Path $root 'publish-next') 'discord-presence-for-codex.exe'
    $stopExe = @($publishExe, $stagingExe) |
        Where-Object { Test-Path -LiteralPath $_ } |
        Select-Object -First 1

    if (Get-RunningPresenceProcesses | Where-Object { -not $_.HasExited }) {
        if ([string]::IsNullOrWhiteSpace($stopExe)) {
            throw "A previous Codex Discord RPC process is running, but no published executable was found to stop it."
        }

        Write-Host 'Stopping the previous Codex Discord RPC process...'
        $stopProcess = Start-Process `
            -FilePath $stopExe `
            -ArgumentList @('--stop') `
            -WorkingDirectory $root `
            -WindowStyle Hidden `
            -Wait `
            -PassThru `
            -ErrorAction Stop
        if ($stopProcess.ExitCode -ne 0) {
            throw "The previous Codex Discord RPC process could not be stopped. Exit code: $($stopProcess.ExitCode)"
        }

        Wait-ForPresenceToStop
    }

    $buildScript = Join-Path $root 'build.cmd'
    if (-not (Test-Path -LiteralPath $buildScript)) {
        throw "Build script not found: $buildScript"
    }

    Write-Host 'Publishing the latest build...'
    & $buildScript
    if ($LASTEXITCODE -ne 0) {
        throw "The latest build failed. Exit code: $LASTEXITCODE"
    }

    if (-not (Test-Path -LiteralPath $publishExe)) {
        throw "Build output not found: $publishExe"
    }

    Write-Host 'Starting the latest published build...'
    $startedProcess = Start-Process `
        -FilePath $publishExe `
        -ArgumentList @('--project', $root) `
        -WorkingDirectory $root `
        -WindowStyle Hidden `
        -PassThru `
        -ErrorAction Stop

    Start-Sleep -Milliseconds 500
    if ($startedProcess.HasExited) {
        throw "The latest published build exited immediately. Exit code: $($startedProcess.ExitCode)"
    }

    Write-Host "Started Codex Discord RPC (PID $($startedProcess.Id))."
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
