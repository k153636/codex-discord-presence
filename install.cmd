@echo off
setlocal
set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"
set "SCRIPT_PATH=%ROOT_DIR%\scripts\InstallShortcuts.ps1"
set "COMMAND_SCRIPT=%ROOT_DIR%\scripts\InstallCodexRpcCommand.ps1"

if not exist "%SCRIPT_PATH%" (
  echo Install script not found: "%SCRIPT_PATH%"
  exit /b 1
)
if not exist "%COMMAND_SCRIPT%" (
  echo Command install script not found: "%COMMAND_SCRIPT%"
  exit /b 1
)

set "AUTOSTART_FLAG="
if /I "%~1"=="--autostart" set "AUTOSTART_FLAG=-Autostart"

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_PATH%" -RootDir "%ROOT_DIR%" %AUTOSTART_FLAG%
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -File "%COMMAND_SCRIPT%" -RootDir "%ROOT_DIR%"
if errorlevel 1 exit /b 1
