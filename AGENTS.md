# AGENTS.md

## Project purpose

This repository is a .NET 9 Windows Forms tray application that publishes the
current Codex session to Discord Rich Presence. The application must favor
observable Codex log evidence and a stable, readable Discord line over guesses
from filesystem timestamps or generic fallback labels.

When making a change, act as a design and implementation partner: inspect the
existing behavior, identify the smallest maintainable change, explain important
trade-offs, and validate the actual published application when the change
affects Discord output.

## Repository layout

- `Application/`: process entrypoint, profile setup, and application lifetime.
- `Infrastructure/`: runtime loop, settings reload, project switching, tray,
  diagnostics, persistence, and single-instance coordination.
- `Activity/`: Codex process detection, session JSONL parsing, activity event
  state machine, operation resolution, agent tracking, and edited-file tracking.
- `Presence/`: template rendering, activity-line composition, status labels,
  edited-file selection, MCP server-name formatting, and main-agent role text.
- `Discord/`: Discord RPC client, asset-key resolution, and party metadata.
- `Configuration/`: options, profile selection, paths, and timing settings.
- `Model/`, `TokenUsage/`, `Project/`, and `Git/`: model/token, project, and
  repository inspection providers.
- `Assets/RpcArt/`: source RPC art. Preserve animated GIFs as GIFs; do not
  bulk-convert the source pack to PNG.
- `Updates/` and `Utilities/`: release checks, formatting, refresh policies,
  reconnect backoff, and other shared helpers.
- `CodexDiscordPresence.Tests/`: xUnit tests for parser, state, rendering,
  configuration, Discord metadata, and filesystem behavior.
- `Preview/`: reference Discord preview images.
- `appsettings.json`: Codex desktop profile. `appsettings.cli.json`: Codex CLI
  profile. `publish/`, `publish-next/`, `bin/`, and `obj/` are generated or
  local staging output and must not be committed.

There is no `Modules/` directory in this project. Keep new code in the existing
area that owns the responsibility instead of introducing a parallel structure.

## Presence behavior contract

The presence state is one semantic activity line. Do not concatenate a
thinking summary with MCP, file, or command text in the same line.

### Evidence and precedence

1. Prefer structured, current Codex session events and direct tool targets.
2. A current reasoning summary replaces the generic working/planning label and
   is shown by itself after normalization.
3. A current MCP operation replaces the reasoning/file/command label and is
   shown by itself. Nested MCP calls inside a `custom_tool_call` are supported,
   including calls recorded with `status: completed`.
4. After a completed MCP call, keep the MCP identity for only the short grace
   period implemented by the state machine; the next effective Codex event
   wins. Never keep an old MCP label indefinitely.
5. If direct Codex evidence is unavailable, fall back to the existing recent
   edited-file and timestamp evidence. Do not invent a current file or expose
   a full local path.

The state machine's `TriggerEvent` represents the latest display-relevant event;
`ActiveOperationEvent` exists for resolving the underlying operation. Preserve
that distinction when changing activity detection.

### MCP display

- One active server: `MCP chrome-devtools`.
- Multiple active distinct servers: `MCP chrome-devtools＆+3`.
- The fullwidth ampersand `＆` is intentional and must remain exactly as shown.
- The `+N` value excludes the representative server and counts only other
  currently active distinct servers, not historical calls.
- Use `McpServerNameFormatter` so names are safe, compact, and path-free.
- Do not append `Editing`, `Reading`, a tool name, or a thinking summary to an
  MCP line.

### File and other activity display

- File operations use the natural `Editing` form, such as `Editing README.md`
  or `Editing src/PresenceRuntime.cs`.
- For four or more active file targets, show one active file followed by
  `+ N files`, where `N` excludes that displayed file.
- `Coordinating` reports the target count and does not pretend to know an
  active filename.
- Preserve existing labels and image selection for reasoning, planning,
  researching, running commands, creating/deleting files, waiting, ready,
  stalled, and offline states. Do not use `Run Command` as a substitute for a
  more specific MCP or Codex event.
- Researching is a distinct active state for observable web/research events;
  it must not be conflated with generic Working.

### Party and model metadata

- Discord party metadata is omitted for a solo session.
- When active subagents exist, use the session-derived party size directly:
  `Party.Size == Party.Max == partySize`. The activity line still represents
  the main agent; do not expose subagent thinking summaries as the main state.
- Format model slugs for Discord as readable words, for example
  `gpt-5.6-luna` -> `gpt 5.6 luna`, then append the detected reasoning effort.
- Append `1.5x` only for the supported GPT model families when the latest
  project-matching session reports an effective `priority` or `fast` service
  tier. Do not display the literal word `fast`, and do not infer speed from a
  config-only value.

## Discord assets and configuration

- Keep the internal Discord asset keys stable (`rpc_codex`, `rpc_thinking`,
  `rpc_coding`, `rpc_reading`, `rpc_searching`, `rpc_building`, and so on).
- The GIF files in `Assets/RpcArt/` are source assets and must remain animated
  GIFs. External image URLs in the settings provide animated fallbacks where
  Discord's uploaded-asset restrictions require them; static internal keys
  remain the fallback path.
- Change `appsettings.json` and `appsettings.cli.json` together when the
  setting is shared by both profiles, unless the change is intentionally
  profile-specific.
- Keep user-specific overrides, tokens, and session data under the local
  application data directory (`%LOCALAPPDATA%\CodexDiscordPresence`), never in
  the repository.

## Build, run, and validation

Run commands from the repository root:

- `dotnet test CodexDiscordPresence.Tests\CodexDiscordPresence.Tests.csproj`
  runs the full xUnit suite.
- `build.cmd` publishes the Release `win-x64` single-file application to
  `publish/`.
- `start.cmd` stops a previous published instance, publishes the latest source,
  and starts the latest published executable. Use `start-cli.cmd` for the CLI
  profile.
- `stop.cmd` stops the desktop-profile instance. `stop-cli.cmd` is the CLI
  stop helper.
- `install.cmd --autostart` installs shortcuts and Windows startup
  registration; `uninstall.cmd` removes them.

For presence changes, the minimum validation is:

1. Run the relevant focused tests, then the full test suite.
2. Build the Release publish and verify the executable path is the current
   `publish\discord-presence-for-codex.exe`.
3. Start that published executable and inspect its log under
   `%LOCALAPPDATA%\CodexDiscordPresence\logs`.
4. For Discord-facing changes, confirm `Discord RPC initialized`, confirm the
   expected `Presence rendered` state, and ensure no Discord RPC initialization
   or update failure was logged. A parser-only or renderer-only test is not
   enough to claim endpoint delivery.

Do not restart the running executable during ordinary source editing unless the
user authorizes it or the workflow has reached the Build/verification stage.
Do not commit generated `publish/`, `publish-next/`, `bin/`, or `obj/` output.

## Coding conventions

- Use four-space indentation, nullable-aware C#, implicit usings, file-scoped
  namespaces, and the line-ending style of neighboring files.
- Use PascalCase for types, methods, and public members; camelCase for locals
  and parameters; `_camelCase` for private fields.
- Prefer small, focused classes and records with explicit null handling.
- Keep template-driven configuration intact. Do not hard-code a new Discord
  display string when an existing template or formatter owns that concern.
- Keep the activity parser, state machine, resolver, composer, and RPC client
  responsibilities separate. Avoid moving logic across those boundaries just
  to make one test pass.

## Testing conventions

- Add or update xUnit tests for every behavior change, especially precedence,
  stale-event, MCP, party, and formatting changes.
- Use descriptive names such as
  `Render_CompletedNestedMcpCall_UsesMcpIdentityAfterToolReturns`.
- Isolate session and filesystem tests with unique temporary directories and
  clean them up in `finally` blocks.
- For session parsing, include the real JSONL shape being fixed (including
  nested `custom_tool_call` input and completion status), not only a synthetic
  state-machine event.
- Test both positive and negative boundaries: solo versus party, one versus
  multiple MCP servers, direct versus fallback file targets, and fresh versus
  stale events.

## Git and safety rules

- Inspect `git status` before editing. Existing dirty changes belong to the
  user; preserve them and do not reset, checkout, or overwrite unrelated work.
- Every logically independent behavior change, bug fix, or refactor is a
  mandatory commit boundary. After its relevant validation passes, create one
  focused imperative commit immediately; do not bundle unrelated work or wait
  to accumulate multiple logical changes. Do not consider the boundary
  complete until its commit exists unless the user explicitly opts out.
- Before each commit, inspect both the unstaged and staged diffs, run
  `git diff --check`, and stage only the files and hunks belonging to that
  logical boundary. Preserve unrelated dirty changes and never include
  generated output in the commit.
- If a logical boundary overlaps existing user changes and cannot be isolated
  safely, preserve the existing changes and stop for explicit direction rather
  than creating a mixed commit.
- Do not commit API keys, access tokens, personal session logs, or local
  application state. Do not push unless the user explicitly asks for a push.
- Do not perform external browser/account changes as part of a local code task
  unless the user explicitly puts that action in scope.
- Never shut down or reboot the PC unless the user explicitly requests it in
  the current task. A command returning without an error is not proof that the
  machine has physically powered off.
