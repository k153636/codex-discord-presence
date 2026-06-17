@echo off
setlocal
set "ROOT_DIR=%~dp0"
set "APP_EXE=%ROOT_DIR%bin\Debug\net9.0\win-x64\discord-presence-for-codex.exe"

if not exist "%APP_EXE%" (
  echo Build output not found: "%APP_EXE%"
  echo Run build.cmd first.
  exit /b 1
)

"%APP_EXE%" --stop
