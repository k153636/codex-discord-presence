@echo off
setlocal
set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "SCRIPT_PATH=%ROOT_DIR%\scripts\UninstallShortcuts.ps1"

if not exist "%SCRIPT_PATH%" (
  echo Uninstall script not found: "%SCRIPT_PATH%"
  exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%" -RootDir "%ROOT_DIR%"
