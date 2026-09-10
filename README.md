# K's Codex RPC

Show Codex CLI / Codex Desktop activity in Discord RPC from a local Windows tray app.

The app displays Codex's current work in Discord Rich Presence.
Beyond generic “Thinking” or “Editing” labels, it can show reasoning summaries available from Codex sessions, active MCP servers, edited files, command execution, and research activity.

<p>
  <a href="https://github.com/k153636/codex-discord-presence/releases/latest"><img src="https://img.shields.io/github/v/release/k153636/codex-discord-presence?display_name=tag&style=for-the-badge&label=Download" alt="Latest release"></a>
  <a href="https://k153636.github.io/codex-discord-presence/"><img src="https://img.shields.io/badge/Website-K%27s%20Codex%20RPC-5865F2?style=for-the-badge&logo=googlechrome&logoColor=white" alt="Website"></a>
  <a href="https://github.com/k153636/codex-discord-presence"><img src="https://img.shields.io/badge/Source-GitHub-181717?style=for-the-badge&logo=github&logoColor=white" alt="Source repository"></a>
  <a href="https://github.com/k153636/codex-discord-presence/blob/main/LICENSE"><img src="https://img.shields.io/github/license/k153636/codex-discord-presence?style=for-the-badge&label=License" alt="MIT License"></a>
</p>

## What it can show in Discord

- Reasoning summaries available from Codex sessions
- The active MCP server
- Edited files and file counts
- Reading, editing, planning, research, and command activity
- The active model and reasoning effort
- Session duration, token usage, and estimated cost when available
- Project information and Git change counts
- Party information for active subagents
- Buttons such as a link to the project website

The app updates Discord RPC using available Codex session events and process signals.

## Codex analysis and state detection happen locally

Codex session data, project information, and Git data are analyzed locally on Windows.

However, the app is not completely offline.
It communicates with Discord Desktop for RPC and may check GitHub Releases for updates when update checks are enabled.

This project does not operate an account system, session server, or cloud-based team dashboard.

The content shown in Discord may include project names, file names, and reasoning summaries. See the [data flow and privacy notes](https://k153636.github.io/codex-discord-presence/privacy.html) before using the app.

## Requirements

- Windows x64
- .NET 9 Desktop Runtime
- Discord Desktop
- Codex CLI or Codex Desktop

## Installation

1. Install the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
2. Start Discord Desktop.
3. Download `discord-presence-for-codex.exe` from the [latest GitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest).
4. Run the executable.

The app stays in the Windows system tray.
Use the tray menu to enable or disable Discord RPC, open the Dashboard, edit settings, or quit the app.

## Codex CLI and Codex Desktop

The app supports separate detection settings for Desktop and CLI.

It checks the running Codex process, session logs, and CLI command-line information to update the presence for the environment currently in use.

## More information

- [Website](https://k153636.github.io/codex-discord-presence/)
- [FAQ](https://k153636.github.io/codex-discord-presence/faq.html)
- [Compatibility](https://k153636.github.io/codex-discord-presence/compatibility.html)
- [Data flow](https://k153636.github.io/codex-discord-presence/privacy.html)
- [Latest GitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest)

## Notes

This project is not an official OpenAI or Discord product.

The current release targets Windows x64.
Token usage, cost, and rate-limit information may not be available in every environment.

MIT License
