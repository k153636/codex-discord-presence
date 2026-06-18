@echo off
setlocal
set "ROOT_DIR=%~dp0"
set "APP_DIR=%ROOT_DIR%publish"
set "APP_EXE=%APP_DIR%\discord-presence-for-codex.exe"

tasklist /fi "IMAGENAME eq discord-presence-for-codex.exe" /fo csv /nh | findstr /i /c:"discord-presence-for-codex.exe" >nul
if not errorlevel 1 (
  echo Codex Discord RPC is already running.
  exit /b 0
)

call "%ROOT_DIR%build.cmd"
if errorlevel 1 exit /b 1

tasklist /fi "IMAGENAME eq discord-presence-for-codex.exe" /fo csv /nh | findstr /i /c:"discord-presence-for-codex.exe" >nul
if not errorlevel 1 (
  echo Codex Discord RPC is already running.
  exit /b 0
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%APP_EXE%' -ArgumentList '--project','%ROOT_DIR%' -WorkingDirectory '%ROOT_DIR%' -WindowStyle Hidden"
echo Started Codex Discord RPC.
