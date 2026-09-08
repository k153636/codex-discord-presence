function Resolve-CodexRpcRoot {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir
    )

    return [System.IO.Path]::GetFullPath($RootDir).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar)
}

function Get-CodexRpcPublishedPaths {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir
    )

    $root = Resolve-CodexRpcRoot -RootDir $RootDir
    $publishExe = Join-Path (Join-Path $root 'publish') 'discord-presence-for-codex.exe'
    $stagingExe = Join-Path (Join-Path $root 'publish-next') 'discord-presence-for-codex.exe'
    $stopExe = @($publishExe, $stagingExe) |
        Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } |
        Select-Object -First 1

    return [PSCustomObject]@{
        RootDir = $root
        StopExe = $stopExe
    }
}

function Get-RunningPresenceProcesses {
    @(Get-Process -Name 'discord-presence-for-codex' -ErrorAction SilentlyContinue)
}

function Wait-ForCodexRpcStop {
    param(
        [int]$TimeoutSeconds = 15
    )

    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        $running = @(Get-RunningPresenceProcesses | Where-Object { -not $_.HasExited })
        if ($running.Count -eq 0) {
            return
        }

        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $deadline)

    $pids = (Get-RunningPresenceProcesses | Select-Object -ExpandProperty Id) -join ', '
    throw "The previous Codex Discord RPC process did not exit within $TimeoutSeconds seconds. PID(s): $pids"
}

function Stop-CodexRpcProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$RootDir
    )

    $paths = Get-CodexRpcPublishedPaths -RootDir $RootDir
    $running = @(Get-RunningPresenceProcesses | Where-Object { -not $_.HasExited })
    if ($running.Count -eq 0) {
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($paths.StopExe)) {
        throw "A previous Codex Discord RPC process is running, but no published executable was found to stop it."
    }

    $stopProcess = Start-Process `
        -FilePath $paths.StopExe `
        -ArgumentList @('--stop') `
        -WorkingDirectory $paths.RootDir `
        -WindowStyle Hidden `
        -Wait `
        -PassThru `
        -ErrorAction Stop
    if ($stopProcess.ExitCode -ne 0) {
        throw "The previous Codex Discord RPC process could not be stopped. Exit code: $($stopProcess.ExitCode)"
    }

    Wait-ForCodexRpcStop
    return $true
}
