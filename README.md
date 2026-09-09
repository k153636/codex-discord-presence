# K's Codex RPC

An unofficial Windows tray application that turns observable Codex CLI and Codex Desktop activity into one readable Discord Rich Presence line.

The application runs locally. It reads the configured Codex session and project signals, resolves the current activity, and publishes the configured presence through Discord Desktop's local RPC client.

- Website: https://k153636.github.io/codex-discord-presence/
- Source: https://github.com/k153636/codex-discord-presence
- Latest release: https://github.com/k153636/codex-discord-presence/releases/latest
- FAQ: https://k153636.github.io/codex-discord-presence/faq.html
- Compatibility: https://k153636.github.io/codex-discord-presence/compatibility.html
- Data flow: https://k153636.github.io/codex-discord-presence/privacy.html

This is a community project. It is not affiliated with or endorsed by OpenAI or Discord.

## What it does

K's Codex RPC keeps a single semantic activity line in Discord while Codex is working. Depending on the available evidence and your templates, the activity can include:

- A current reasoning summary
- The active MCP server
- An edited file or the number of coordinated files
- A command or research state
- The current model and reasoning effort
- Session tokens, estimated cost, project metadata, Git counts, timestamps, and buttons

The published Windows profile detects Codex Desktop. The separate CLI profile detects Codex CLI command-line signatures and uses the same local session and project evidence.

## Requirements

- Windows x64
- .NET 9 Desktop Runtime
- Discord Desktop running for local Rich Presence delivery
- Codex Desktop or Codex CLI, depending on the profile you use

The current release is a framework-dependent single-file executable. Download discord-presence-for-codex.exe from the latest GitHub Release.

## Install and run

1. Install the .NET 9 Desktop Runtime if it is not already installed.
2. Start Discord Desktop.
3. Download and run discord-presence-for-codex.exe.
4. Right-click the tray icon to use Enable, Edit Discord RPC, or Quit.

The application stores its local state and logs under %LOCALAPPDATA%\CodexDiscordPresence.

## Build from source

Run these commands from the repository root in PowerShell or Command Prompt.

    build.cmd

This publishes the Release win-x64 build to:

    publish\discord-presence-for-codex.exe

To publish and start the latest source for the desktop workflow:

    start.cmd

The CLI workflow has its own helper and settings profile:

    start-cli.cmd

Both start helpers stop a previous published instance, publish the current source, and start the published executable. To stop the running instance:

    stop.cmd
    stop-cli.cmd

To install Desktop and Start Menu shortcuts:

    install.cmd

Add --autostart to create a Windows Startup shortcut as well:

    install.cmd --autostart

Remove those shortcuts with:

    uninstall.cmd

The installer also provides the optional PowerShell commands codex-rpc and codex-rpc-stop. codex-rpc launches the existing published executable without rebuilding; run build.cmd when source changes need to be published. codex-rpc-stop stops the running instance without starting a replacement. Reopen PowerShell after installation or removal so the updated user PATH is loaded.

## Presence behavior

The presence has one display line. The resolver prefers evidence in this order:

1. Current structured Codex session events and direct tool targets
2. A current reasoning summary, shown by itself after normalization
3. A current MCP operation, shown by itself
4. Recent edited-file and timestamp evidence when direct Codex evidence is unavailable

After an MCP call completes, its identity is kept only for the short grace period in the state machine. The next effective Codex event replaces it. The fallback path does not claim that the app observed a current tool action, and full local paths are not exposed in the activity line.

### MCP

A single active server is shown like:

    MCP chrome-devtools

Multiple distinct active servers are shown like:

    MCP chrome-devtools＆+3

The fullwidth ampersand ＆ is intentional. The +N count includes only other active distinct servers, not historical calls. MCP lines do not append a tool name, Editing, Reading, or a thinking summary.

### Files and other states

File activity uses a natural label such as:

    Editing README.md
    Editing src/PresenceRuntime.cs

When four or more files are active, the line shows one file followed by the remaining count, for example Editing README.md + 3 files. Coordination reports a file count without inventing an active filename.

Other configured states include Planning, Thinking, Working, Researching, Run Command, Creating files, Deleting files, Refactoring, Waiting, Idling, Ready, Stalled, and Error. Researching is kept distinct from generic working. A current MCP operation takes precedence over normal reasoning, file, and command labels.

### Models, tokens, and party metadata

Model slugs are formatted for Discord as readable words. For example, gpt-5.6-luna becomes gpt 5.6 luna, followed by the detected reasoning effort when available.

The 1.5x suffix appears only for supported GPT model families when the latest session matching the active project reports an effective priority or fast service tier. The literal word fast is not displayed, and a config-only value does not prove the effective tier.

Party metadata is omitted for a solo session. When active subagents exist, the session-derived party size is used for both Party.Size and Party.Max; the activity line still represents the main agent.

Token usage is read from a session whose cwd exactly matches the active project path. The application does not borrow another project's total when there is no exact match.

## Images and assets

The source RPC art lives in Assets/RpcArt/.

- rpc_codex is the small image.
- rpc_thinking represents reasoning and planning.
- rpc_coding covers edits, file creation/deletion, refactoring, and general build or command work.
- rpc_reading covers Git commands.
- rpc_searching covers search commands and research.
- rpc_debugging covers test commands.
- rpc_sleeping covers offline, waiting, ready, and idle states.
- rpc_success is used for a short hold after a successful Codex turn.
- rpc_error is reserved for an explicitly failed terminal event.
- rpc_deploying is reserved for future event-specific states.

The source GIFs remain animated GIFs. Discord asset uploads have format restrictions, so configured external image URLs provide animated fallbacks while stable internal asset keys remain the fallback path. The public site uses the same named art files under docs/assets.

## Configuration

The executable reads configuration in this order:

1. appsettings.json for the Desktop profile
2. appsettings.cli.json for the CLI profile
3. %LOCALAPPDATA%\CodexDiscordPresence\user-settings.json for user overrides

The two repository settings files contain the shared Discord asset mappings and the profile-specific Codex detection rules. User overrides, tokens, session data, logs, and the saved tray state stay outside the repository.

Common settings include:

- Discord: client id, image keys, external image URLs, completed/error image behavior
- Codex and CodexCli: Codex home, process or command-line detection, and session scan limits
- Project: project path, Git-root preference, file scan limits, recent-file tracking, and ignored patterns
- Presence: model detection, details/state templates, activity labels, buttons, and timing thresholds
- TokenUsage: optional token and cost values
- UpdateIntervalSeconds and EnableUpdateCheck

The executable also accepts these command-line overrides:

    --client-id <id>
    --project <path>
    --interval <seconds>
    --model <name>

Useful template values include:

- {ModelName}, {CodexStatus}, {CodexProcessName}, and {GoalModePrefix}
- {ProjectName}, {ProjectPath}, {ProjectFileCount}, {ProjectLineCount}, and {ProjectSizeText}
- {EditingFileName}, {EditingFileLabel}, {EditingFilePath}, {ActiveEditedFileCount}, and {ActiveEditedFilesText}
- {ChangedFileCount} and {ChangedFilesText}
- {ActivityLabel}, {ActivityKind}, {ActivityConfidence}, {ActivityProvenance}, {ActivityReason}, and {ActivityLine}
- {ThinkingSummary}, {RunningCommandName}, and {RunningCommandKind}
- {SessionElapsed}, {SessionStartedAt}, {Tokens}, {Cost}, {BillingType}, and {RateLimitDetails}

The default button label is K's Codex RPC.

## Local data flow

The application reads local:

- Codex session JSONL under the configured Codex home
- Codex configuration and model environment variables
- Process, window, and CLI command-line signals
- Project and Git metadata
- Local settings and the saved tray enable state

It publishes the configured details, state, images, timestamps, party metadata when applicable, and buttons to Discord Desktop through its local RPC client. Depending on your templates and current evidence, the payload can include a project name, file label, model, reasoning effort, activity summary, MCP server name, command kind, token text, or Git count.

The release checker can call the public GitHub Releases API to look for a newer release. Configured external image URLs may be requested by the site preview or referenced by the Discord presence payload. This project does not operate a hosted account, session database, team dashboard, or analytics backend.

Review your Discord settings and custom templates before sharing sensitive project names, file labels, or reasoning summaries.

## Project layout

- Application/: WinForms entrypoint, tray lifetime, and dashboard controls
- Infrastructure/: runtime loop, profile selection, settings reload, persistence, diagnostics, and single-instance coordination
- Activity/: Codex process detection, JSONL parsing, activity state, operation resolution, agent tracking, and edited-file tracking
- Presence/: activity-line composition, templates, status labels, file selection, MCP formatting, and main-agent role text
- Discord/: Discord RPC client, asset-key resolution, and party metadata
- Configuration/: options, profile selection, paths, and timing settings
- Model/, TokenUsage/, Project/, and Git/: model, token, project, and repository providers
- Updates/ and Utilities/: release checks, formatting, refresh policies, and shared helpers
- Assets/RpcArt/: source RPC art
- docs/: the public static site
- CodexDiscordPresence.Tests/: xUnit coverage for parsing, state, rendering, configuration, Discord metadata, and filesystem behavior

Generated publish output (publish/, publish-next/) and build output (bin/, obj/) are local staging artifacts and should not be committed.

## Validation

Run the full test suite from the repository root:

    dotnet test CodexDiscordPresence.Tests\CodexDiscordPresence.Tests.csproj

For a release-style executable check:

    build.cmd

The published executable should be:

    publish\discord-presence-for-codex.exe

Runtime diagnostics are written under:

    %LOCALAPPDATA%\CodexDiscordPresence\logs

When validating Discord delivery, look for Discord RPC initialized and the expected Presence rendered record, and confirm that no Discord RPC initialization or update failure was logged.

## Limits

The current release does not promise:

- macOS or Linux binaries
- A hosted dashboard, team account, or session service
- Guaranteed live quota values for every Codex plan
- Official OpenAI or Discord affiliation

For the current compatibility boundary, see the website compatibility notes: https://k153636.github.io/codex-discord-presence/compatibility.html

