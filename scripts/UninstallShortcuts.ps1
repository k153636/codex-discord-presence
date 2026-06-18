param(
    [Parameter(Mandatory = $true)]
    [string]$RootDir
)

$ErrorActionPreference = 'Stop'

function Remove-AppShortcut {
    param(
        [Parameter(Mandatory = $true)][string]$ShortcutPath
    )

    if (Test-Path -LiteralPath $ShortcutPath) {
        Remove-Item -LiteralPath $ShortcutPath -Force
    }
}

try {
    $desktopDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::DesktopDirectory)
    $startMenuProgramsDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::StartMenu)) 'Programs'
    $startupDir = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)

    $shortcutName = 'Codex Discord RPC.lnk'

    Remove-AppShortcut -ShortcutPath (Join-Path $desktopDir $shortcutName)
    Remove-AppShortcut -ShortcutPath (Join-Path $startMenuProgramsDir $shortcutName)
    Remove-AppShortcut -ShortcutPath (Join-Path $startupDir $shortcutName)

    Write-Host "Removed Codex Discord RPC shortcuts."
}
catch {
    Write-Error $_.Exception.Message
    exit 1
}
