@echo off
setlocal
if exist "%~dp0publish" rmdir /s /q "%~dp0publish"
dotnet publish "%~dp0discord-presence-for-codex.csproj" -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o "%~dp0publish"
