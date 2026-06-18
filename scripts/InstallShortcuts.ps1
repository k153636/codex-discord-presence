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

    $shortcutName = 'Codex Discord RPC.lnk'
    $arguments = "--project `"$root`""

    New-AppShortcut -ShortcutPath (Join-Path $desktopDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments
    New-AppShortcut -ShortcutPath (Join-Path $startMenuProgramsDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments

    if ($Autostart) {
        New-AppShortcut -ShortcutPath (Join-Path $startupDir $shortcutName) -TargetPath $publishExe -WorkingDirectory (Split-Path $publishExe) -Arguments $arguments
    }

    Write-Host "Installed shortcuts for Codex Discord RPC."
    if ($Autostart) {
        Write-Host "Autostart shortcut created in the Windows Startup folder."
    }
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
