# K's Code Presence

K's Code Presence — AI coding status for Discord.

K's Code Presence is an unofficial Windows tray app that turns observable Codex, Claude Code, and Antigravity CLI activity into one clear Discord Rich Presence.
Discord follows the AI coding tool you are actually using. Codex works out of the box; Claude Code and Antigravity CLI are opt-in from the Dashboard.

> Thinking summaries are available for Codex only. Claude Code and Antigravity CLI show general status labels.

<p>
  <a href="https://github.com/k153636/codex-discord-presence/releases/latest"><img src="https://img.shields.io/github/v/release/k153636/codex-discord-presence?display_name=release&sort=date&style=for-the-badge&label=Download" alt="Latest release"></a>
  <a href="https://k153636.github.io/codex-discord-presence/"><img src="https://img.shields.io/badge/Website-K%27s%20Code%20Presence-5865F2?style=for-the-badge&logo=googlechrome&logoColor=white" alt="K's Code Presence website"></a>
  <a href="https://github.com/k153636/codex-discord-presence"><img src="https://img.shields.io/badge/Source-GitHub-181717?style=for-the-badge&logo=github&logoColor=white" alt="Source repository"></a>
  <a href="https://github.com/k153636/codex-discord-presence/blob/main/LICENSE"><img src="https://img.shields.io/github/license/k153636/codex-discord-presence?style=for-the-badge&label=License" alt="MIT License"></a>
</p>

## Supported tools

| Tool | Integration | What Discord can show |
| --- | --- | --- |
| Codex CLI & Desktop | Built in: local session logs and process signals | Reasoning summaries, MCP servers, edited files, model and effort, tokens, subagent party and observed work status |
| Claude Code | Opt-in: observational hooks in `~/.claude/settings.json` | Model, effort, MCP, files, terminal spinner labels, Claude Design, subagent party and child tool status |
| Antigravity CLI | Opt-in: official statusLine payload | Model and effort, plan and quota usage, tool activity, and reported subagents with explicit status when available |

Enabling an integration does not force it to take over; the tool with current, project-matching activity is shown.

Automatic switching waits at least five seconds after Discord acknowledges the current tool's first activity. The Dashboard also holds a tool for five seconds after its first activity card is drawn. Activity updates within the same tool remain live; repeated acknowledgments do not restart the hold. If no acknowledgment arrives, the wait expires after 30 seconds so switching cannot stall indefinitely.

## What it can show in Discord

- Reasoning summaries (Codex only)
- The active MCP server
- Edited files and file counts
- Reading, editing, planning, research, and command activity
- The active model and reasoning effort
- Session duration, plus token usage and estimated cost when available
- Project information and Git change counts
- Party information for active subagents
- A small image and tooltip for confirmed active subagents; a work category appears only when child activity evidence identifies it
- A project website button labeled `K's Code RPC`

## Analysis happens locally

Session data, project information, and Git data are analyzed locally on Windows.

However, the app is not completely offline.
It communicates with Discord Desktop for RPC and may check GitHub Releases for updates when update checks are enabled.

This project does not operate an account system, session server, or cloud-based team dashboard.

The content shown in Discord may include project names, file names, and reasoning summaries. See the [data flow and privacy notes](https://k153636.github.io/codex-discord-presence/privacy.html) before using the app.

## Requirements

- Windows x64
- .NET 9 Desktop Runtime
- Discord Desktop
- Codex CLI, Codex Desktop, Claude Code, or Antigravity CLI

## Installation

1. Install the [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0).
2. Start Discord Desktop.
3. Download `discord-presence-for-codex.exe` from the [latest GitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest).
4. Run the executable.

The app stays in the Windows system tray.
Use the tray menu to enable or disable Discord Rich Presence, open the Dashboard, edit K's Code Presence settings, or quit the app.

The next packaged distribution also supports [automatic updates](docs/automatic-updates.md):
background downloads, a restart after the app is idle, postponement, and manual
update controls in the tray. Historical standalone EXEs need a one-time move to
the Setup installer or portable ZIP. See the [version migration](docs/version-history.md).

The next Setup installer and portable ZIP include the .NET 10 Desktop Runtime;
users do not need to install .NET separately. Source builds require the .NET 10
SDK, and `build.cmd` also produces a self-contained executable. The installation
steps above still apply to the currently published .NET 9 standalone release.

## Codex CLI and Codex Desktop

The app supports separate detection settings for Desktop and CLI.

It checks the running Codex process, session logs, and CLI command-line information to update the presence for the environment currently in use.

## Claude Code

Claude Code support is opt-in from the Dashboard. The app adds observational hooks to `~/.claude/settings.json`, keeps a local backup of that file, and removes only its own hook definitions when the integration is disabled. Prompts, tool output, and source text are discarded. Tokens, cost, and quota are not shown for Claude Code.

## Antigravity CLI

Antigravity support is opt-in from the Dashboard. The integration reads only the official statusLine JSON payload, groups observations by `conversation_id`, and treats `thinking`, `working`, and `tool_use` as active states. It omits Discord party metadata and the subagent small image for solo sessions, and publishes a party only when the payload explicitly reports active `subagents`; `task_count` is never used as a subagent count because it represents background work rather than confirmed subagents. The tooltip reports specific child work only when the payload supplies an explicit status; otherwise it reports the active count without guessing.

Antigravity CLI uses its own Discord application configuration and the `rpc_antigravity_cli` static art asset; the existing `rpc_antigravity` asset remains reserved for a future Antigravity desktop application. Codex application IDs and asset mappings are not reused.

If Antigravity already has a user-owned `statusLine` setting, the application reports a conflict and leaves that setting unchanged.

## More information

- [Website](https://k153636.github.io/codex-discord-presence/)
- [FAQ](https://k153636.github.io/codex-discord-presence/faq.html)
- [Compatibility](https://k153636.github.io/codex-discord-presence/compatibility.html)
- [Data flow](https://k153636.github.io/codex-discord-presence/privacy.html)
- [Latest GitHub Release](https://github.com/k153636/codex-discord-presence/releases/latest)

## Notes

This project is not an official OpenAI, Anthropic, Google, or Discord product.

The current release targets Windows x64.
Token usage, cost, and rate-limit information may not be available in every environment, and are not shown for Claude Code.

MIT License
