@echo off
setlocal
dotnet publish "%~dp0discord-presence-for-codex.csproj" -c Release -r win-x64 --self-contained true -o "%~dp0publish"
