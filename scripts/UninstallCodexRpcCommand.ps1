$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CodexRpcCommand.Common.ps1')

try {
    $paths = Get-CodexRpcCommandPaths
    $managedPaths = @(
        $paths.CommandPath,
        $paths.LauncherPath,
        $paths.StopCommandPath,
        $paths.StopLauncherPath,
        $paths.LegacyQuitCommandPath,
        $paths.LegacyQuitLauncherPath,
        $paths.LegacyLauncherPath)
    foreach ($path in $managedPaths) {
        Assert-CodexRpcManagedFile -Path $path -Marker $paths.Marker
    }

    $binPathKey = Get-CodexRpcCanonicalPath -Path $paths.BinDir
    $pathEntries = @(Get-CodexRpcUserPathEntries)
    $pathWithoutBin = @(
        $pathEntries | Where-Object { (Get-CodexRpcCanonicalPath -Path $_) -ne $binPathKey }
    )
    $newUserPath = $pathWithoutBin -join ';'
    $currentUserPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    $pathChanged = $currentUserPath -ne $newUserPath

    if ($pathChanged) {
        [Environment]::SetEnvironmentVariable('Path', $newUserPath, 'User')
    }

    foreach ($path in $managedPaths) {
        if (Test-Path -LiteralPath $path) {
            Remove-Item -LiteralPath $path -Force
        }
    }

    if (Test-Path -LiteralPath $paths.BinDir) {
        $remainingItems = @(Get-ChildItem -LiteralPath $paths.BinDir -Force)
        if ($remainingItems.Count -eq 0) {
            Remove-Item -LiteralPath $paths.BinDir -Force
        }
    }

    Write-Host 'Removed the PowerShell commands: codex-rpc, codex-rpc-stop'
    if ($pathChanged) {
        Write-Host 'User PATH updated. Open a new PowerShell session to finish removing codex-rpc.'
    }
    else {
        Write-Host 'The launcher directory was not present in the user PATH.'
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
