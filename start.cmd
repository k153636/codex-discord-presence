@echo off
setlocal
set "ROOT_DIR=%~dp0"
if "%ROOT_DIR:~-1%"=="\" set "ROOT_DIR=%ROOT_DIR:~0,-1%"

powershell -NoProfile -ExecutionPolicy Bypass -File "%ROOT_DIR%\scripts\StartLatestBuild.ps1" -RootDir "%ROOT_DIR%"
if errorlevel 1 exit /b 1

echo Latest Codex Discord RPC build is running.
