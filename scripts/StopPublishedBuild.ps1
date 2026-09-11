param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexRpcProcess.Common.ps1')

try {
    $stopped = Stop-CodexRpcProcess -RootDir $RootDir
    if ($stopped) {
        Write-Host "Stopped K's Code Presence."
    }
    else {
        Write-Host "No running K's Code Presence instance was found."
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
