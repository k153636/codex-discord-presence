param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir,
    [switch]$Autostart
)

$ErrorActionPreference = 'Stop'

function New-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath,
        [Parameter(Mandatory = $true)][string]$TargetPath,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [Parameter(Mandatory = $true)][string]$Arguments
    )

    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $TargetPath
    $shortcut.WorkingDirectory = $WorkingDirectory
    $shortcut.Arguments = $Arguments
    $shortcut.IconLocation = "$TargetPath,0"
    $shortcut.Save()
}

function Get-AppShortcutTargetPath {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath
    )

    if (-not (Test-Path -LiteralPath $ShortcutPath -PathType Leaf)) {
        return $null
    }

    try {
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($ShortcutPath)
        return [System.IO.Path]::GetFullPath($shortcut.TargetPath)
    }
    catch {
        return $null
    }
}

function Migrate-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$LegacyShortcutPath,
        [Parameter(Mandatory = $true)][string]$NewShortcutPath,
        [Parameter(Mandatory = $true)][string]$ExpectedTargetPath
    )

    $targetPath = Get-AppShortcutTargetPath -ShortcutPath $LegacyShortcutPath
    $expectedTargetPath = [System.IO.Path]::GetFullPath($ExpectedTargetPath)
    if (-not [string]::Equals($targetPath, $expectedTargetPath, [StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if (Test-Path -LiteralPath $NewShortcutPath) {
        $newTargetPath = Get-AppShortcutTargetPath -ShortcutPath $NewShortcutPath
        if ([string]::Equals($newTargetPath, $expectedTargetPath, [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $LegacyShortcutPath -Force
        }

        return
    }
    else {
        Move-Item -LiteralPath $LegacyShortcutPath -Destination $NewShortcutPath
    }
}

try {
    $root = [System.IO.Path]::GetFullPath($RootDir).TrimEnd([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    $publishExe = Join-Path (Join-Path $root 'publish') 'discord-presence-for-codex.exe'
    if (-not (Test-Path -LiteralPath $publishExe)) {
        throw "Build output not found: $publishExe. Run build.cmd first."
    }

    $desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $startMenuProgramsDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs'
    $startupDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)

    New-Item -ItemType Directory -Force -Path $startMenuProgramsDir | Out-Null
    if ($Autostart) {
        New-Item -ItemType Directory -Force -Path $startupDir | Out-Null
    }

    $shortcutName = "K's Code Presence.lnk"
    $legacyShortcutNames = @("K's AIcode presence.lnk", 'CodePresence.lnk', 'Codex Discord RPC.lnk')
    $arguments = "--project `"$root`""

    New-AppShortcut -ShortcutPath (Join-Path $desktopDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments
    New-AppShortcut -ShortcutPath (Join-Path $startMenuProgramsDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments

    if ($Autostart) {
        New-AppShortcut -ShortcutPath (Join-Path $startupDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments
    }

    foreach ($legacyShortcutName in $legacyShortcutNames) {
        Migrate-AppShortcut -LegacyShortcutPath (Join-Path $desktopDir $legacyShortcutName) -NewShortcutPath (Join-Path $desktopDir $shortcutName) -ExpectedTargetPath $publishExe
        Migrate-AppShortcut -LegacyShortcutPath (Join-Path $startMenuProgramsDir $legacyShortcutName) -NewShortcutPath (Join-Path $startMenuProgramsDir $shortcutName) -ExpectedTargetPath $publishExe
        Migrate-AppShortcut -LegacyShortcutPath (Join-Path $startupDir $legacyShortcutName) -NewShortcutPath (Join-Path $startupDir $shortcutName) -ExpectedTargetPath $publishExe
    }

    Write-Host "Installed shortcuts for K's Code Presence."
    if ($Autostart) {
        Write-Host "Autostart shortcut created in the Windows Startup folder."
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
