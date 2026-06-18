@echo off
setlocal
set "ROOT_DIR=%~dp0"
set "APP_EXE=%ROOT_DIR%publish\discord-presence-for-codex.exe"

if not exist "%APP_EXE%" (
  call "%ROOT_DIR%build.cmd"
  if errorlevel 1 exit /b 1
)

powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Process -FilePath '%APP_EXE%' -ArgumentList '--cli','--stop' -WorkingDirectory '%ROOT_DIR%' -WindowStyle Hidden"
