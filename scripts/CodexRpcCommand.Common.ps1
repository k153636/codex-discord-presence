function Get-CodexRpcCommandPaths {
    $localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
    $binDir = Join-Path (Join-Path $localAppData 'CodexDiscordPresence') 'bin'

    return [PSCustomObject]@{
        Marker = 'CodexDiscordPresenceLauncher'
        BinDir = $binDir
        CommandPath = Join-Path $binDir 'codex-rpc.cmd'
        LauncherPath = Join-Path $binDir 'codex-rpc-launcher.ps1'
        LegacyLauncherPath = Join-Path $binDir 'codex-rpc.ps1'
    }
}

function Get-CodexRpcCanonicalPath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    $expandedPath = [Environment]::ExpandEnvironmentVariables($Path.Trim().Trim('"'))
    if ([string]::IsNullOrWhiteSpace($expandedPath)) {
        return ''
    }
    try {
        return [System.IO.Path]::GetFullPath($expandedPath).TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar).ToUpperInvariant()
    }
    catch {
        return $expandedPath.TrimEnd(
            [System.IO.Path]::DirectorySeparatorChar,
            [System.IO.Path]::AltDirectorySeparatorChar).ToUpperInvariant()
    }
}

function Get-CodexRpcUserPathEntries {
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ([string]::IsNullOrEmpty($userPath)) {
        return @()
    }

    return @($userPath -split ';')
}

function Assert-CodexRpcManagedFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$Marker
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    $content = Get-Content -LiteralPath $Path -Raw
    if ($content -notmatch [regex]::Escape($Marker)) {
        throw "Refusing to modify an unmanaged file: $Path"
    }
}
