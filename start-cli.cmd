@echo off
setlocal
set "ROOT_DIR=%~dp0"
set "APP_DIR=%ROOT_DIR%publish"
set "APP_EXE=%APP_DIR%\discord-presence-for-codex.exe"

call "%ROOT_DIR%build.cmd"
if errorlevel 1 exit /b 1

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%APP_EXE%' -ArgumentList '--cli','--project','%ROOT_DIR%' -WorkingDirectory '%ROOT_DIR%' -WindowStyle Hidden"
echo Started Codex CLI Discord RPC.
