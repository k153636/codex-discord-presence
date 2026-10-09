# 0.5.0

Release preparation for K's Code Presence on Windows x64. Publishing the release
is a separate step from building these packages.

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
- Displays the original Claude Design dashboard through WebView2 and adds
  installer, portable, and automatic-update packages with the .NET 10 Desktop
  Runtime included. WebView2 Runtime and Discord Desktop are required.
- Includes third-party notices and the provider artwork licenses in packages.

Historical standalone executables remain .NET 9 builds. Install the Setup or
portable distribution once to gain automatic-update support. Settings remain
under `%LOCALAPPDATA%\CodexDiscordPresence`.

Claude hook changes may require approval or a new Claude session. The app does
not restart Claude or access Claude account credentials. Estimated session cost
is not an actual subscription charge.
