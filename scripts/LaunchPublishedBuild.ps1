param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [string[]]$Arguments = @()
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
    $publishExe = Join-Path (Join-Path $root 'publish') 'discord-presence-for-codex.exe'

    if (-not (Test-Path -LiteralPath $publishExe -PathType Leaf)) {
        throw "Published build not found: $publishExe. Run build.cmd first."
    }

    if (Get-RunningPresenceProcesses | Where-Object { -not $_.HasExited }) {
        Write-Host 'Stopping the previous Codex Discord RPC process...'
        $stopProcess = Start-Process `
            -FilePath $publishExe `
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

    $build = Get-Item -LiteralPath $publishExe
    Write-Host "Starting the latest existing published build (built $($build.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss')))..."
    $processArguments = @('--project', "`"$root`"")
    if ($Arguments) {
        $processArguments += $Arguments
    }
    $startedProcess = Start-Process `
        -FilePath $publishExe `
        -ArgumentList $processArguments `
        -WorkingDirectory $root `
        -WindowStyle Hidden `
        -PassThru `
        -ErrorAction Stop

    Start-Sleep -Milliseconds 500
    if ($startedProcess.HasExited) {
        throw "The published build exited immediately. Exit code: $($startedProcess.ExitCode)"
    }

    Write-Host "Started Codex Discord RPC (PID $($startedProcess.Id))."
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
