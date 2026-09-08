@echo off
setlocal
if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
if exist "%~dp0publish" (
  echo Failed to remove the previous publish output: "%~dp0publish"
  exit /b 1
)
if exist "%~dp0publish-next" rmdir /s /q "%~dp0publish-next"
if exist "%~dp0publish-next" (
  echo Failed to remove stale publish staging output: "%~dp0publish-next"
  exit /b 1
)
dotnet publish "%~dp0discord-presence-for-codex.csproj" -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o "%~dp0publish"
