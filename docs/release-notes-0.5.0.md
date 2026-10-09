# 0.5.0

K's Code Presence 0.5.0 for Windows x64. Download the standalone EXE from the [GitHub Release](https://github.com/k153636/codex-discord-presence/releases/tag/v0.5.0).

- Adds opt-in Claude Code and Antigravity CLI integrations alongside Codex
  Desktop and CLI, using current provider activity and a shared switching gate.
- Claude Code uses the same Token formatting and five-second waiting cycle as
  Codex. Main-session transcripts supply cumulative tokens; matching official
  statusLine observations supply estimated cost, five-hour usage, and reset time.
  Missing values remain unknown. Existing terminal output is preserved.
- Preserves Japanese project paths through Claude hook input. Tokens remain
  available when statusLine installation encounters a conflict.
- Detects fresh Antigravity CLI activity across project boundaries without
  borrowing another provider's Git, recent-file, party, or usage metadata.
- Displays the original Claude Design dashboard through WebView2. Its original
  HTML and animated artwork are embedded in the standalone EXE. The .NET 10
  Desktop Runtime, WebView2 Runtime, and Discord Desktop are required separately.
- Includes third-party notices and the provider artwork licenses in packages.

Historical releases remain .NET 9 builds. This distribution supports update
notifications; quit the tray app and replace the EXE manually. Settings remain
under `%LOCALAPPDATA%\CodexDiscordPresence`.

Claude hook changes may require approval or a new Claude session. The app does
not restart Claude or access Claude account credentials. Estimated session cost
is not an actual subscription charge.
