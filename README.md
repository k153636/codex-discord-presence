# Discord Presence for Codex

Discord Rich Presence for making Codex look like the active worker, not the user's current tab or editor state.

The displayed text is template-driven through `appsettings.json`, so the copy can be changed without code edits.

## Setup

1. Build with `build.cmd`.
2. Start with `start.cmd`.
3. Stop with `stop.cmd`.
4. Set `Discord:ClientId` only if you want to use a different Discord application.

## What It Detects

- Current Codex model when available
- Project name and project size
- Recent edited file and multi-file editing bursts
- Git changed-file count
- Session elapsed time
- Token count and estimated cost placeholders
- AI activity labels inferred from session logs, file writes, and Git diff behavior

## Default Presence

- `Details`: `{ModelName} working on {ProjectName}`
- `State`: `{ActivityLine}`
- `LargeImageText`: `{ProjectSizeText} ・ session {SessionElapsed}`
- `SmallImageText`: `{Tokens} ・ est. {EstimatedCost}`
- Button: `GitHub`

## Model Detection

When `Presence.AutoDetectModelName` is enabled, the app resolves `{ModelName}` from:

- `CODEX_MODEL`, `OPENAI_MODEL`, or `MODEL_NAME`
- Recent Codex session JSONL files under `CODEX_HOME` or `%USERPROFILE%\.codex`
- `%USERPROFILE%\.codex\config.toml`
- `Presence.ModelName` as the fallback

The app also logs:

- Selected UI model
- Last used session model
- Final displayed model

## Activity Labels

The current state engine prefers these labels:

- `Analyzing`
- `Planning`
- `Applying edits`
- `Refactoring`
- `Ready`

The labels and their surrounding copy stay configurable in `appsettings.json`.

## Ready-to-Run Build

The project is configured for a self-contained `win-x64` single-file publish.

- `build.cmd` publishes the app
- `start.cmd` launches the published exe in the background
- `stop.cmd` shuts down the running instance

## Template Values

`Presence.Details`, `Presence.State`, `Presence.LargeImageText`, `Presence.SmallImageText`, and button labels/URLs can use these placeholders:

- `{ModelName}`
- `{CodexStatus}`
- `{CodexProcessName}`
- `{ProjectName}`
- `{ProjectPath}`
- `{ProjectFileCount}`
- `{ProjectLineCount}`
- `{ProjectSizeText}`
- `{EditingFileName}`
- `{EditingFileLabel}`
- `{EditingFilePath}`
- `{ChangedFileCount}`
- `{ChangedFilesText}`
- `{ActivityLabel}`
- `{ActivityKind}`
- `{ActivityProvenance}`
- `{ActivityReason}`
- `{ActivityLine}`
- `{SessionElapsed}`
- `{SessionStartedAt}`
- `{Tokens}`
- `{EstimatedCost}`
